using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed class LunarTransfer {
        public const double ParkingAltitude=300;
        public const double CruiseStep=20;
        static readonly double CruiseTime=4*86400;
        public double BurnTime,ArrivalTime,PredictedCapture;
        public Vector3D DeparturePosition,DepartureVelocity,ArrivalPosition,MoonAtArrival;
        public Vector3D[] Path;
        public static StackDesign StarterStack(Mission destination){
            return destination.Body.Lunar?new StackDesign{Core=1,Engine=1}:new StackDesign();
        }
        public static Mission EarthLaunch(Mission destination,SolarSystem epoch){
            if(!destination.Body.Lunar)return destination;
            // Park in the plane of the Moon's projected path. Choose the phase
            // so the departure opportunity occurs within the first revolution.
            var relative=epoch.Positions[4]-epoch.Positions[3];
            var motion=epoch.Velocities[4]-epoch.Velocities[3];
            var plane=Orbit.FromState(Body.Earth,relative,motion,epoch.Time);
            var later=epoch.Copy();later.Advance(CruiseTime);
            var moon=later.Positions[4]-later.Positions[3];
            double angle=Math.Atan2(Vector3D.DotProduct(moon,plane.Rotate(0,1)),Vector3D.DotProduct(moon,plane.Rotate(1,0)));
            return new Mission(destination.Name,Body.Earth,ParkingAltitude,ParkingAltitude,
                plane.Inc*180/Math.PI,plane.Node*180/Math.PI,(plane.Arg+angle-2.64-.71)*180/Math.PI,
                destination.Hours,destination.Dose,destination.Budget,destination.Instrument,destination.Objective,destination.Experiment);
        }
        public static double FirstDeparture(Flight f){
            if(f.Current==null||f.Current.Pe<160||f.Current.Ap>1800)return double.NaN;
            var projected=f.System.Copy();projected.Advance(CruiseTime);
            var moon=projected.Positions[4]-projected.Positions[3];
            double angle=Math.Atan2(Vector3D.DotProduct(moon,f.Current.Rotate(0,1)),Vector3D.DotProduct(moon,f.Current.Rotate(1,0)))-2.69;
            double anomaly=2*Math.Atan2(Math.Sqrt(1-f.Current.E)*Math.Sin(angle/2),Math.Sqrt(1+f.Current.E)*Math.Cos(angle/2));
            double wait=Orbit.Wrap(anomaly-f.Current.E*Math.Sin(anomaly)-f.Current.Mean(f.Time))*f.Current.Period/Orbit.Circle;
            return wait>f.Current.Period-6?0:wait;
        }
        static double C(double z){
            if(z>1e-6){double s=Math.Sqrt(z);return (1-Math.Cos(s))/z;}
            if(z< -1e-6){double s=Math.Sqrt(-z);return (Math.Cosh(s)-1)/(-z);}
            return .5-z/24+z*z/720;
        }
        static double S(double z){
            if(z>1e-6){double s=Math.Sqrt(z);return (s-Math.Sin(s))/(s*s*s);}
            if(z< -1e-6){double s=Math.Sqrt(-z);return (Math.Sinh(s)-s)/(s*s*s);}
            return 1d/6-z/120+z*z/5040;
        }
        static Vector3D Lambert(Vector3D start,Vector3D end,double time){
            double a=start.Length,b=end.Length;
            double cos=Vector3D.DotProduct(start,end)/(a*b);
            double geometry=Math.Sqrt(Math.Max(1, a*b*(1+cos)));
            double low=-16,high=Orbit.Circle*Orbit.Circle*.99;
            double y=0;
            for(int i=0;i<80;i++){
                double z=(low+high)*.5,c=C(z),s=S(z);
                y=a+b+geometry*(z*s-1)/Math.Sqrt(c);
                if(y<=0){low=z;continue;}
                double x=Math.Sqrt(y/c);
                double elapsed=(x*x*x*s+geometry*Math.Sqrt(y))/Math.Sqrt(Body.Earth.GM);
                if(elapsed>time)high=z;else low=z;
            }
            return (end-start*(1-y/a))/(geometry*Math.Sqrt(y/Body.Earth.GM));
        }
        sealed class TrajectoryGrid {
            readonly Vector3D[][] bodies;
            public readonly Vector3D MoonVelocity;
            public Vector3D Moon {get{return bodies[bodies.Length-1][4];}}
            public TrajectoryGrid(SolarSystem epoch){
                var solar=epoch.Copy();
                bodies=new Vector3D[(int)(CruiseTime/CruiseStep)+1][];
                for(int i=0;i<bodies.Length;i++){
                    var earth=solar.Positions[3];
                    var offsets=new Vector3D[SolarSystem.GM.Length];
                    for(int j=0;j<offsets.Length;j++)offsets[j]=solar.Positions[j]-earth;
                    bodies[i]=offsets;
                    if(i+1<bodies.Length)solar.Advance(CruiseStep);
                }
                MoonVelocity=solar.Velocities[4]-solar.Velocities[3];
            }
            public int Count {get{return bodies.Length;}}
            static Vector3D Pull(Vector3D p,double gm){
                double r=p.LengthSquared;return p*(gm/(r*Math.Sqrt(r)));
            }
            public Vector3D Acceleration(int step,Vector3D p,double blend){
                var total=Pull(-p,Body.Earth.GM);
                if(blend==0)return total;
                var state=bodies[step];
                for(int i=0;i<state.Length;i++){
                    if(i==3)continue;
                    total+=(Pull(state[i]-p,SolarSystem.GM[i])-Pull(state[i],SolarSystem.GM[i]))*blend;
                }
                return total;
            }
        }
        static Vector3D Propagate(TrajectoryGrid grid,Vector3D position,Vector3D velocity,out Vector3D finalVelocity,double blend=1,List<Vector3D> trace=null){
            var acceleration=grid.Acceleration(0,position,blend);
            if(trace!=null)trace.Add(position);
            for(int i=1;i<grid.Count;i++){
                const double step=CruiseStep;
                position+=velocity*step+acceleration*(step*step*.5);
                var next=grid.Acceleration(i,position,blend);
                velocity+=(acceleration+next)*(step*.5);
                acceleration=next;
                if(trace!=null&&i%75==0)trace.Add(position);
            }
            if(trace!=null)trace.Add(position);
            finalVelocity=velocity;
            return position;
        }
        static bool Correct(TrajectoryGrid solar,Vector3D initial,Vector3D goal,ref Vector3D velocity,out Vector3D arrivalVelocity,double blend=1){
            arrivalVelocity=new Vector3D();
            const double epsilon=.00007;
            Vector3D error=new Vector3D();
            for(int i=0;i<25;i++){
                var end=Propagate(solar,initial,velocity,out arrivalVelocity,blend);
                error=goal-end;
                if(error.Length<.03){arrivalVelocity-=solar.MoonVelocity;return true;}
                var x=(Propagate(solar,initial,velocity+new Vector3D(epsilon,0,0),out arrivalVelocity,blend)-end)/epsilon;
                var y=(Propagate(solar,initial,velocity+new Vector3D(0,epsilon,0),out arrivalVelocity,blend)-end)/epsilon;
                var z=(Propagate(solar,initial,velocity+new Vector3D(0,0,epsilon),out arrivalVelocity,blend)-end)/epsilon;
                double det=Vector3D.DotProduct(x,Vector3D.CrossProduct(y,z));
                if(Math.Abs(det)<1e-6||double.IsNaN(det)){
                    Failure="Departure geometry was singular.";
                    return false;
                }
                var change=new Vector3D(Vector3D.DotProduct(error,Vector3D.CrossProduct(y,z)),
                    Vector3D.DotProduct(x,Vector3D.CrossProduct(error,z)),Vector3D.DotProduct(x,Vector3D.CrossProduct(y,error)))/det;
                if(change.Length>.35)change*=.35/change.Length;
                double fraction=1;
                while(fraction>=.015625){
                    var check=Propagate(solar,initial,velocity+fraction*change,out arrivalVelocity,blend);
                    if((goal-check).Length<error.Length){velocity+=fraction*change;break;}
                    fraction*=.5;
                }
                if(fraction<.015625)break;
            }
            Failure=error.Length.ToString("N1")+" km residual";
            return false;
        }
        public static string Failure;
        public static LunarTransfer Calculate(SolarSystem epoch,Vector3D position,Orbit target){
            var solar=new TrajectoryGrid(epoch);
            var moon=solar.Moon;
            double peri=target.Body.Radius+target.Pe;
            var guess=Lambert(position,moon,CruiseTime);
            Vector3D approximateArrival;
            Propagate(solar,position,guess,out approximateArrival,0);
            var approach=solar.MoonVelocity-approximateArrival;
            double angle=Math.Atan2(Vector3D.DotProduct(approach,target.Rotate(0,1)),Vector3D.DotProduct(approach,target.Rotate(1,0)));
            foreach(double offset in new[]{.42,-.42,0d,.85,-.85}){
                double bearing=angle+offset;
                var radial=target.Rotate(Math.Cos(bearing),Math.Sin(bearing));
                var goal=moon+radial*peri;
                var velocity=Lambert(position,goal,CruiseTime);
                Vector3D incoming=new Vector3D();
                bool converged=true;
                for(int step=0;step<=20;step++){
                    if(Correct(solar,position,goal,ref velocity,out incoming,step/20d))continue;
                    converged=false;break;
                }
                if(!converged)continue;
                var markers=new List<Vector3D>();
                Vector3D ignored;
                Propagate(solar,position,velocity,out ignored,1,markers);
                var tangent=target.Rotate(-Math.Sin(bearing),Math.Cos(bearing));
                double speed=Math.Sqrt(Body.Moon.GM/peri);
                return new LunarTransfer {BurnTime=epoch.Time,ArrivalTime=epoch.Time+CruiseTime,
                    DeparturePosition=position,DepartureVelocity=velocity,ArrivalPosition=radial*peri,MoonAtArrival=moon,
                    PredictedCapture=(tangent*speed-incoming).Length,Path=markers.ToArray()};
            }
            return null;
        }
    }
}

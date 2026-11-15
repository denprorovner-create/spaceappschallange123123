using System;
using System.Collections.Generic;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed class MarsTransferPlan {
        public double BurnTime, ArrivalTime, FlightDays, EntrySpeed, EntryFlightPathAngle, ClosestAltitude;
        public double EstimatedLandingErrorKm;
        public Vector3D DeltaVelocity;
        public Vector3D[] Path;
    }
    sealed class MarsCruise {
        public const double Day=86400, ParkingAltitude=300, MarsRadius=3389.5, InterfaceAltitude=125;
        public const double MarsRotation=88642.6848;
        public const double SiteElevation=-8.208;
        public const double SiteLatitude=-32.91015625,SiteLongitude=62.19921875;
        const double EntryRadius=MarsRadius+SiteElevation+InterfaceAltitude;
        const double MatchRadius=55000, TargetAngle=-12.4*Math.PI/180;
        const double DefaultEntryDownrange=750;
        const int Earth=3,Mars=5;
        static double MuMars {get{return SolarSystem.GM[Mars];}}
        static readonly Lazy<Tuple<double,double>> defaultWindow=new Lazy<Tuple<double,double>>(SearchWindow);
        public static double DefaultDepartureDays {get{return defaultWindow.Value.Item1;}}
        public static double DefaultFlightDays {get{return defaultWindow.Value.Item2;}}
        public static readonly DateTime Epoch=new DateTime(2026,11,14);
        public static DateTime Date(double time){return Epoch.AddSeconds(time);}
        public static SolarSystem LaunchEpoch(double offsetDays){
            var system=new SolarSystem();
            system.Advance(Math.Max(0,offsetDays)*Day);
            return system;
        }
        static double Clamp(double value,double lower,double upper){return Math.Min(upper,Math.Max(lower,value));}
        static Vector3D Unit(Vector3D v){
            double length=v.Length;
            return length>1e-12?v/length:new Vector3D(1,0,0);
        }
        static Vector3D Combine(Vector3D first,Vector3D second,double angle){
            return first*Math.Cos(angle)+second*Math.Sin(angle);
        }
        // Lambert's zero-revolution solution supplies a seed. All displayed and flown
        // paths come from the separate N-body integrator.
        static double StumpffC(double z) {
            if(z>1e-5){double r=Math.Sqrt(z);return (1-Math.Cos(r))/z;}
            if(z< -1e-5){double r=Math.Sqrt(-z);return (Math.Cosh(r)-1)/(-z);}
            return .5-z/24+z*z/720;
        }
        static double StumpffS(double z) {
            if(z>1e-5){double r=Math.Sqrt(z);return (r-Math.Sin(r))/(r*r*r);}
            if(z< -1e-5){double r=Math.Sqrt(-z);return (Math.Sinh(r)-r)/(r*r*r);}
            return 1d/6-z/120+z*z/5040;
        }
        static bool Lambert(Vector3D from,Vector3D to,double seconds,out Vector3D initial,out Vector3D final){
            initial=final=new Vector3D();
            double r1=from.Length,r2=to.Length,mu=SolarSystem.GM[0];
            double cosine=Clamp(Vector3D.DotProduct(from,to)/(r1*r2),-1,1);
            double geometry=Math.Sqrt(r1*r2*(1+cosine));
            if(geometry<1)return false;
            if(Vector3D.CrossProduct(from,to).Y>0)geometry=-geometry;
            double low=-64,high=4*Math.PI*Math.PI-.0001,radius=0,elapsed=0;
            for(int i=0;i<72;i++){
                double z=(low+high)*.5,c=StumpffC(z),s=StumpffS(z);
                radius=r1+r2+geometry*(z*s-1)/Math.Sqrt(c);
                if(radius<=0){low=z;continue;}
                double x=Math.Sqrt(radius/c);
                elapsed=(x*x*x*s+geometry*Math.Sqrt(radius))/Math.Sqrt(mu);
                if(elapsed>seconds)high=z;else low=z;
            }
            if(Math.Abs(elapsed-seconds)>2)return false;
            double g=geometry*Math.Sqrt(radius/mu);
            initial=(to-from*(1-radius/r1))/g;
            final=(to*(1-radius/r2)-from)/g;
            return true;
        }
        static Tuple<double,double> SearchWindow(){
            const int end=455;
            var states=new SolarSystem[end+1];
            var solar=new SolarSystem();
            for(int day=0;day<states.Length;day++){
                states[day]=solar.Copy();if(day<end)solar.Advance(Day);
            }
            double best=double.PositiveInfinity;int departure=0,duration=230;
            for(int d=0;d<=155;d+=3)for(int tof=165;tof<=300;tof+=3){
                var start=states[d];var finish=states[d+tof];
                Vector3D vd,va;
                if(!Lambert(start.Positions[Earth]-start.Positions[0],finish.Positions[Mars]-finish.Positions[0],tof*Day,out vd,out va))continue;
                double earthCost=(vd-(start.Velocities[Earth]-start.Velocities[0])).Length;
                double marsCost=(va-(finish.Velocities[Mars]-finish.Velocities[0])).Length;
                double score=Math.Sqrt(2*Body.Earth.GM/(Body.Earth.Radius+ParkingAltitude)+earthCost*earthCost)+.17*marsCost+.002*tof;
                if(score<best){best=score;departure=d;duration=tof;}
            }
            return Tuple.Create((double)departure,(double)duration);
        }
        static bool HeliocentricSeed(SolarSystem epoch,double days,out Vector3D departure,out Vector3D approach){
            var end=epoch.Copy();end.Advance(days*Day);
            if(!Lambert(epoch.Positions[Earth]-epoch.Positions[0],end.Positions[Mars]-end.Positions[0],days*Day,out departure,out approach))return false;
            departure-=epoch.Velocities[Earth]-epoch.Velocities[0];
            approach-=end.Velocities[Mars]-end.Velocities[0];
            return true;
        }
        static Vector3D EscapeRadial(Vector3D asymptote,double radius,Vector3D normal){
            var direction=Unit(asymptote);
            double e=1+radius*asymptote.LengthSquared/Body.Earth.GM;
            return Combine(direction,Unit(Vector3D.CrossProduct(normal,direction)),-Math.Acos(-1/e));
        }
        public static Mission EarthParking(SolarSystem epoch,double flightDays){
            Vector3D departure,approach;
            if(!HeliocentricSeed(epoch,flightDays,out departure,out approach))departure=new Vector3D(1,0,0);
            var direction=Unit(departure);
            var pole=new Vector3D(0,-1,0);
            var normal=Unit(pole-direction*Vector3D.DotProduct(pole,direction));
            double radius=Body.Earth.Radius+ParkingAltitude;
            var radial=EscapeRadial(departure,radius,normal);
            var tangential=Unit(Vector3D.CrossProduct(normal,radial));
            var orbit=Orbit.FromState(Body.Earth,radial*radius,tangential*(Math.Sqrt(Body.Earth.GM/radius)*1.000001),epoch.Time);
            return new Mission("Hellas Expedition",Body.Earth,ParkingAltitude,ParkingAltitude,orbit.Inc*180/Math.PI,
                orbit.Node*180/Math.PI,(orbit.Arg-.7)*180/Math.PI,24,.2,150,0,
                "Deliver a rover to Hellas Planitia.","Investigate surface conditions and the basin's geological history.");
        }
        static Orbit ParkingOrbit(Flight parking){
            var orbit=Orbit.FromState(Body.Earth,parking.Position,parking.Velocity,parking.System.Time);
            return parking.Mission.Body==Body.Earth&&orbit!=null&&orbit.Pe>=150&&orbit.Ap<=2200?orbit:null;
        }
        public static double FindDeparture(Flight parking,double flightDays){
            var orbit=ParkingOrbit(parking);
            if(orbit==null)return double.NaN;
            Vector3D departure,approach;
            if(!HeliocentricSeed(parking.System,flightDays,out departure,out approach))return double.NaN;
            var normal=Unit(Vector3D.CrossProduct(orbit.Rotate(1,0),orbit.Rotate(0,1)));
            var projected=departure-normal*Vector3D.DotProduct(departure,normal);
            if(projected.Length<1e-8)return double.NaN;
            var radial=EscapeRadial(projected,parking.Position.Length,normal);
            double angle=Math.Atan2(Vector3D.DotProduct(radial,orbit.Rotate(0,1)),Vector3D.DotProduct(radial,orbit.Rotate(1,0)));
            double anomaly=2*Math.Atan2(Math.Sqrt(1-orbit.E)*Math.Sin(angle/2),Math.Sqrt(1+orbit.E)*Math.Cos(angle/2));
            double wait=Orbit.Wrap(anomaly-orbit.E*Math.Sin(anomaly)-orbit.Mean(parking.Time))*orbit.Period/Orbit.Circle;
            return wait>orbit.Period-10?0:wait;
        }
        static Vector3D EarthEscape(Vector3D position,Vector3D asymptote){
            var radial=Unit(position);var direction=Unit(asymptote);
            double cosine=Clamp(Vector3D.DotProduct(radial,direction),-1,1);
            double sine=Math.Sqrt(Math.Max(1e-12,1-cosine*cosine));
            double radius=position.Length,mu=Body.Earth.GM,speed=asymptote.Length;
            double k=radius*speed*sine;
            double h=.5*(k+Math.Sqrt(k*k+4*mu*radius*(1-cosine)));
            double radialSpeed=speed*cosine+mu/h*sine;
            return radial*radialSpeed+Unit(direction-radial*cosine)*(h/radius);
        }
        static double MarsHyperbolaAngle(double speed,double radius,double angle){
            double h=EntryRadius*Math.Sqrt(speed*speed+2*MuMars/EntryRadius)*Math.Cos(angle);
            double e=Math.Sqrt(1+speed*speed*h*h/(MuMars*MuMars));
            double asymptoticAngle=Math.Acos(-1/e);
            double trueAngle=Math.Acos(Clamp((h*h/(MuMars*radius)-1)/e,-1,1));
            return asymptoticAngle-trueAngle;
        }
        // Hermite interpolation of positions from the shared N-body solar simulation.
        // It supplies continuous planetary positions and velocities to shooting and flight.
        sealed class Ephemeris {
            const double Spacing=1800;
            readonly Vector3D[,] positions,velocities;
            public readonly double Start,End;
            public Ephemeris(SolarSystem initial,double seconds){
                Start=initial.Time;
                var system=initial.Copy();
                int count=(int)Math.Ceiling(seconds/Spacing)+2;
                End=Start+(count-1)*Spacing;
                positions=new Vector3D[count,SolarSystem.GM.Length];velocities=new Vector3D[count,SolarSystem.GM.Length];
                for(int i=0;i<count;i++){
                    for(int j=0;j<SolarSystem.GM.Length;j++){positions[i,j]=system.Positions[j];velocities[i,j]=system.Velocities[j];}
                    if(i+1<count)system.Advance(Spacing);
                }
            }
            void Interval(double time,out int index,out double u) {
                double t=Clamp((time-Start)/Spacing,0,positions.GetLength(0)-1d);
                index=Math.Min(positions.GetLength(0)-2,(int)t);u=t-index;
            }
            public Vector3D Position(int body,double time){int index;double u;Interval(time,out index,out u);return At(body,index,u);}
            Vector3D At(int body,int i,double u){
                double u2=u*u,u3=u2*u;
                return (2*u3-3*u2+1)*positions[i,body]+(u3-2*u2+u)*Spacing*velocities[i,body]+
                    (-2*u3+3*u2)*positions[i+1,body]+(u3-u2)*Spacing*velocities[i+1,body];
            }
            public Vector3D Velocity(int body,double time){
                int i;double u;Interval(time,out i,out u);double u2=u*u;
                return (6*u2-6*u)/Spacing*(positions[i,body]-positions[i+1,body])+
                    (3*u2-4*u+1)*velocities[i,body]+(3*u2-2*u)*velocities[i+1,body];
            }
            public Vector3D Acceleration(double time,Vector3D ship,double marsScale){
                int i;double u;Interval(time,out i,out u);
                var total=new Vector3D();
                for(int body=0;body<SolarSystem.GM.Length;body++){
                    if(body==Mars&&marsScale==0)continue;
                    var vector=At(body,i,u)-ship;
                    double r2=Math.Max(1,vector.LengthSquared);
                    double scale=body==Mars?marsScale:1;
                    total+=vector*(scale*SolarSystem.GM[body]/(r2*Math.Sqrt(r2)));
                }
                return total;
            }
        }
        struct State {
            public double Time;
            public Vector3D Position,Velocity;
            public State(double time,Vector3D position,Vector3D velocity){Time=time;Position=position;Velocity=velocity;}
        }
        static double SpeedStep(Ephemeris system,State state){
            double nearest=Math.Min((state.Position-system.Position(Earth,state.Time)).Length,
                (state.Position-system.Position(Mars,state.Time)).Length);
            if(nearest<15000)return 3;
            if(nearest<50000)return 12;
            if(nearest<250000)return 45;
            if(nearest<1000000)return 180;
            if(nearest<5000000)return 600;
            return 5400;
        }
        static State PropagateStep(Ephemeris solar,State current,double dt,double marsScale=1){
            double t=current.Time;var p=current.Position;var v=current.Velocity;
            var a=solar.Acceleration(t,p,marsScale);
            var v1=v+a*(dt*.5);
            var b=solar.Acceleration(t+dt*.5,p+v*(dt*.5),marsScale);
            var v2=v+b*(dt*.5);
            var c=solar.Acceleration(t+dt*.5,p+v1*(dt*.5),marsScale);
            var v3=v+c*dt;
            var d=solar.Acceleration(t+dt,p+v2*dt,marsScale);
            return new State(t+dt,p+(v+2*v1+2*v2+v3)*(dt/6),v+(a+2*b+2*c+d)*(dt/6));
        }
        static State Integrate(Ephemeris solar,State state,double until,double marsScale){
            while(state.Time<until-1e-7)state=PropagateStep(solar,state,Math.Min(until-state.Time,SpeedStep(solar,state)),marsScale);
            return state;
        }
        static bool Correct(Ephemeris solar,State initial,Vector3D target,double time,out State arrival,ref Vector3D velocity,double marsScale=1){
            arrival=initial;
            for(int i=0;i<14;i++){
                initial.Velocity=velocity;
                arrival=Integrate(solar,initial,time,marsScale);
                var error=target-arrival.Position;
                if(error.Length<.18)return true;
                const double probe=.000002;
                var x=Derivative(solar,initial,time,arrival.Position,new Vector3D(probe,0,0),marsScale)/probe;
                var y=Derivative(solar,initial,time,arrival.Position,new Vector3D(0,probe,0),marsScale)/probe;
                var z=Derivative(solar,initial,time,arrival.Position,new Vector3D(0,0,probe),marsScale)/probe;
                double det=Vector3D.DotProduct(x,Vector3D.CrossProduct(y,z));
                if(!(Math.Abs(det)>1e-9))return false;
                var change=new Vector3D(Vector3D.DotProduct(error,Vector3D.CrossProduct(y,z)),
                    Vector3D.DotProduct(x,Vector3D.CrossProduct(error,z)),Vector3D.DotProduct(x,Vector3D.CrossProduct(y,error)))/det;
                if(change.Length>.8)change*=.8/change.Length;
                bool improved=false;
                for(double scale=1;scale>=.0625;scale*=.5){
                    initial.Velocity=velocity+change*scale;
                    if((Integrate(solar,initial,time,marsScale).Position-target).Length>=error.Length)continue;
                    velocity=initial.Velocity;improved=true;break;
                }
                if(!improved)return error.Length<2.5;
            }
            return (arrival.Position-target).Length<2.5;
        }
        static Vector3D Derivative(Ephemeris solar,State initial,double time,Vector3D baseline,Vector3D probe,double marsScale){
            initial.Velocity+=probe;
            return Integrate(solar,initial,time,marsScale).Position-baseline;
        }
        static double RelativeFlightAngle(Vector3D p,Vector3D v){
            return Math.Asin(Clamp(Vector3D.DotProduct(Unit(p),Unit(v)),-1,1))*180/Math.PI;
        }
        static Vector3D MarsAtmosphereVelocity(Vector3D p){
            var pole=MarsPole;
            return Vector3D.CrossProduct(-pole*(2*Math.PI/MarsRotation),p);
        }
        // IAU Mars north pole expressed in the Horizons J2000 ecliptic frame.
        static readonly Vector3D MarsPole=MarsNorth();
        const double PoleRa=317.68143*Math.PI/180,PoleDec=52.88650*Math.PI/180;
        static readonly Vector3D MarsEquinox=FromEquatorial(-Math.Sin(PoleRa),Math.Cos(PoleRa),0);
        static Vector3D FromEquatorial(double x,double y,double z){
            const double eps=23.4392911*Math.PI/180;
            return new Vector3D(x,z*Math.Cos(eps)-y*Math.Sin(eps),y*Math.Cos(eps)+z*Math.Sin(eps));
        }
        static Vector3D MarsNorth(){
            return FromEquatorial(Math.Cos(PoleDec)*Math.Cos(PoleRa),Math.Cos(PoleDec)*Math.Sin(PoleRa),Math.Sin(PoleDec));
        }
        static Vector3D SiteRadial(double time){
            // PDS MOLA uses east-positive planetocentric coordinates. Rotation uses the
            // nominal IAU Mars pole and prime meridian (J2000 SPICE PCK convention).
            double meridian=(176.630+(9813.5+time/Day)*350.89198226+SiteLongitude)*Math.PI/180;
            double latitude=SiteLatitude*Math.PI/180;
            return MarsPole*Math.Sin(latitude)+Combine(MarsEquinox,Vector3D.CrossProduct(MarsEquinox,MarsPole),meridian)*Math.Cos(latitude);
        }
        static double SiteMatchTime(double near,double duration,Vector3D approach,double angle,double downrange){
            var axis=-Unit(approach);
            double landingArc=MarsHyperbolaAngle(approach.Length,EntryRadius,angle)+downrange/(MarsRadius+SiteElevation);
            double target= Math.Cos(landingArc);
            Func<double,double> error=time=>Vector3D.DotProduct(SiteRadial(time+duration),axis)-target;
            double best=near,score=Math.Abs(error(near));
            for(double cursor=near;cursor<near+MarsRotation;cursor+=MarsRotation/40){
                double prior=error(cursor),after=error(cursor+MarsRotation/40);
                if(Math.Abs(prior)<score){score=Math.Abs(prior);best=cursor;}
                if(prior*after>0)continue;
                double low=cursor,high=cursor+MarsRotation/40;
                for(int i=0;i<22;i++){
                    double middle=(low+high)/2;
                    if(error(middle)*prior>0)low=middle;else high=middle;
                }
                return (low+high)/2;
            }
            return best;
        }
        static double LandingError(Ephemeris solar,State entry,double downrange=DefaultEntryDownrange) {
            var p=entry.Position-solar.Position(Mars,entry.Time);
            var v=entry.Velocity-solar.Velocity(Mars,entry.Time)-MarsAtmosphereVelocity(p);
            var radial=Unit(p);var track=Unit(v-radial*Vector3D.DotProduct(v,radial));
            var estimated=Combine(radial,track,downrange/(MarsRadius+SiteElevation));
            // This projection is an entry-corridor estimate. Guidance/control,
            // local terrain and EDL dynamics determine actual touchdown.
            var relativeSite=SiteRadial(entry.Time);
            return Math.Acos(Clamp(Vector3D.DotProduct(estimated,relativeSite),-1,1))*(MarsRadius+SiteElevation);
        }
        static bool CrossesInterface(Ephemeris ephemeris,State state){return (state.Position-ephemeris.Position(Mars,state.Time)).Length<=EntryRadius;}
        static State RefineEntry(Ephemeris ephemeris,State prev,State next){
            double low=0,high=next.Time-prev.Time;
            for(int i=0;i<24;i++){
                double mid=(low+high)*.5;
                if(CrossesInterface(ephemeris,PropagateStep(ephemeris,prev,mid)))high=mid;else low=mid;
            }
            return PropagateStep(ephemeris,prev,high);
        }
        static State Encounter(Ephemeris ephemeris,State state,double end,out bool entry,out double closest,List<Vector3D> path=null){
            entry=false;closest=double.PositiveInfinity;double marker=state.Time;
            while(state.Time<end-1e-7){
                if(path!=null&&state.Time>=marker){
                    path.Add(state.Position-ephemeris.Position(0,state.Time));
                    marker=state.Time+Day*.35;
                }
                var previous=state;
                state=PropagateStep(ephemeris,state,Math.Min(end-state.Time,SpeedStep(ephemeris,state)));
                double marsRange=(state.Position-ephemeris.Position(Mars,state.Time)).Length;
                closest=Math.Min(closest,marsRange-MarsRadius);
                if((state.Position-ephemeris.Position(Earth,state.Time)).Length<Body.Earth.Radius)break;
                if(marsRange>EntryRadius)continue;
                entry=true;state=RefineEntry(ephemeris,previous,state);closest=InterfaceAltitude;break;
            }
            if(path!=null)path.Add(state.Position-ephemeris.Position(0,state.Time));
            return state;
        }
        public static MarsTransferPlan Plan(Flight parking,double flightDays,RoverBuild rover=null){
            return PlanCorridor(parking,flightDays,true,rover)??PlanCorridor(parking,flightDays,false,rover);
        }
        static MarsTransferPlan PlanCorridor(Flight parking,double flightDays,bool aimAtSite,RoverBuild rover){
            if(ParkingOrbit(parking)==null||flightDays<140||flightDays>390)return null;
            var start=parking.System.Copy();
            var startPosition=parking.Position;var startVelocity=parking.Velocity;
            Vector3D departure,approach;
            if(!HeliocentricSeed(start,flightDays,out departure,out approach))return null;
            var solar=new Ephemeris(start,flightDays*Day+5*Day);
            var guess=start.Velocities[Earth]+EarthEscape(startPosition,departure);
            var state=new State(start.Time,start.Positions[Earth]+startPosition,start.Velocities[Earth]+startVelocity);
            double matchTime=start.Time+flightDays*Day;
            State match=new State(),entryState=new State();
            double angle=TargetAngle;
            double downrange=DefaultEntryDownrange;
            double approachDuration=(MatchRadius-EntryRadius)/approach.Length;
            bool entered=false;double gamma=0,closest=0;
            for(int attempt=0;attempt<15;attempt++){
                if(aimAtSite)matchTime=SiteMatchTime(start.Time+flightDays*Day,approachDuration,approach,angle,downrange);
                var axis=Unit(approach);
                var site=aimAtSite?SiteRadial(matchTime+approachDuration):-MarsPole;
                var side=Unit(site-axis*Vector3D.DotProduct(site,axis));
                var offset=Combine(-axis,side,MarsHyperbolaAngle(approach.Length,MatchRadius,angle))*MatchRadius;
                var target=solar.Position(Mars,matchTime)+offset;
                bool solved;
                if(attempt==0){
                    solved=true;
                    for(int ramp=0;ramp<=4;ramp++)
                        if(!Correct(solar,state,target,matchTime,out match,ref guess,ramp/4d)){solved=false;break;}
                }else solved=Correct(solar,state,target,matchTime,out match,ref guess);
                if(!solved){
                    Console.WriteLine("Correct failed site {0} attempt {1} residual {2:F2} km, t+{3:F3} d",aimAtSite,attempt,(match.Position-target).Length,(matchTime-start.Time)/Day);
                    return null;
                }
                state.Velocity=guess;
                entryState=Encounter(solar,match,matchTime+Day,out entered,out closest);
                if(entered){
                    approachDuration=entryState.Time-matchTime;
                    var relative=entryState.Position-solar.Position(Mars,entryState.Time);
                    var incoming=entryState.Velocity-solar.Velocity(Mars,entryState.Time)-MarsAtmosphereVelocity(relative);
                    gamma=RelativeFlightAngle(relative,incoming);
                    if(aimAtSite&&rover!=null&&attempt==0){
                        var environment=new MarsAtmosphere(MarsSiteData.Hellas.Pressure,MarsSiteData.Hellas.Elevation);
                        double speed=incoming.Length*1000;
                        var preview=new MarsFlight(rover,InterfaceAltitude*1000,-speed*Math.Sin(TargetAngle),speed*Math.Cos(TargetAngle),environment);
                        preview.TickEntry(1100);
                        if(preview.Landed)downrange=preview.Downrange/1000;
                    }
                    if(Math.Abs(gamma-TargetAngle*180/Math.PI)<.07&&(!aimAtSite||LandingError(solar,entryState,downrange)<12))break;
                    angle=Clamp(angle+.57*(TargetAngle-gamma*Math.PI/180),-.55,-.03);
                }
                else angle=Math.Max(-.60,angle-.15);
                // Recover asymptotic approach from the computed Mars hyperbola.
                var mp=match.Position-solar.Position(Mars,match.Time);
                var mv=match.Velocity-solar.Velocity(Mars,match.Time);
                var h=Vector3D.CrossProduct(mp,mv);
                double energy=mv.LengthSquared/2-MuMars/mp.Length;
                if(energy>0&&h.Length>1){
                    var ecc=Vector3D.CrossProduct(mv,h)/MuMars-Unit(mp);
                    if(ecc.Length>1){
                        var t=Unit(Vector3D.CrossProduct(h,ecc));
                        approach=-Combine(Unit(ecc),t,-Math.Acos(-1/ecc.Length))*Math.Sqrt(2*energy);
                    }
                }
            }
            if(!entered||gamma> -5||gamma< -24)return null;
            var trace=new List<Vector3D>();
            entryState=Encounter(solar,state,matchTime+Day,out entered,out closest,trace);
            if(!entered)return null;
            var radial=entryState.Position-solar.Position(Mars,entryState.Time);
            var velocity=entryState.Velocity-solar.Velocity(Mars,entryState.Time)-MarsAtmosphereVelocity(radial);
            return new MarsTransferPlan{BurnTime=start.Time,ArrivalTime=entryState.Time,FlightDays=flightDays,
                DeltaVelocity=guess-start.Velocities[Earth]-startVelocity,Path=trace.ToArray(),ClosestAltitude=closest,
                EntrySpeed=velocity.Length,EntryFlightPathAngle=RelativeFlightAngle(radial,velocity),
                EstimatedLandingErrorKm=LandingError(solar,entryState,downrange)};
        }
        readonly Ephemeris ephemeris;
        readonly SolarSystem epochSystem;
        readonly double dryMass,isp,limitTime;
        double queued;
        State spacecraft;
        public double Fuel {get;private set;}
        public double DeltaVUsed {get;private set;}
        public bool BurnAccepted {get;private set;}
        public bool EntryReached {get;private set;}
        public bool Failed {get;private set;}
        public string Status {get;private set;}
        public double Time {get{return spacecraft.Time;}}
        public double StartTime {get{return ephemeris.Start;}}
        public double ElapsedDays {get{return (Time-StartTime)/Day;}}
        public Vector3D Position {get{return spacecraft.Position-ephemeris.Position(0,Time);}}
        public Vector3D Velocity {get{return spacecraft.Velocity-ephemeris.Velocity(0,Time);}}
        public Vector3D MarsPosition {get{return spacecraft.Position-ephemeris.Position(Mars,Time);}}
        public Vector3D MarsVelocity {get{return spacecraft.Velocity-ephemeris.Velocity(Mars,Time);}}
        public double EntryHeightM {get{return (MarsPosition.Length-MarsRadius-SiteElevation)*1000;}}
        public double DownMps {get{return -1000*Vector3D.DotProduct(MarsVelocity,Unit(MarsPosition));}}
        public double HorizontalMps {
            get {
                var air=MarsVelocity-MarsAtmosphereVelocity(MarsPosition);
                return 1000*(air-Unit(MarsPosition)*Vector3D.DotProduct(air,Unit(MarsPosition))).Length;
            }
        }
        public double EntryFlightPathAngle {get{return RelativeFlightAngle(MarsPosition,MarsVelocity-MarsAtmosphereVelocity(MarsPosition));}}
        public double EarthRange {get{return (spacecraft.Position-ephemeris.Position(Earth,Time)).Length;}}
        public double MarsRange {get{return MarsPosition.Length;}}
        public Vector3D Acceleration {get{return ephemeris.Acceleration(Time,spacecraft.Position,1);}}
        public Vector3D SurfaceSun {
            get {
                double latitude,longitude;
                SurfaceCoordinates(Unit(-PlanetPosition(Mars)),out latitude,out longitude);
                double lat=latitude*Math.PI/180,lon=longitude*Math.PI/180;
                return new Vector3D(Math.Cos(lat)*Math.Cos(lon),Math.Sin(lat),Math.Cos(lat)*Math.Sin(lon));
            }
        }
        public void GroundCoordinates(double downrangeMeters,double crossrangeMeters,out double latitude,out double longitude){
            var radial=Unit(MarsPosition);
            var relative=MarsVelocity-MarsAtmosphereVelocity(MarsPosition);
            var forward=Unit(relative-radial*Vector3D.DotProduct(relative,radial));
            var side=Unit(Vector3D.CrossProduct(forward,radial));
            var ground=Combine(radial,forward,downrangeMeters/(1000*(MarsRadius+SiteElevation)));
            ground=Unit(Combine(ground,side,crossrangeMeters/(1000*(MarsRadius+SiteElevation))));
            SurfaceCoordinates(ground,out latitude,out longitude);
        }
        void SurfaceCoordinates(Vector3D ground,out double latitude,out double longitude){
            latitude=Math.Asin(Clamp(Vector3D.DotProduct(ground,MarsPole),-1,1))*180/Math.PI;
            var east=Vector3D.CrossProduct(MarsEquinox,MarsPole);
            double inertial=Math.Atan2(Vector3D.DotProduct(ground,east),Vector3D.DotProduct(ground,MarsEquinox))*180/Math.PI;
            double meridian=176.630+(9813.5+Time/Day)*350.89198226;
            longitude=((inertial-meridian)%360+360)%360;
        }
        public SolarSystem System {get{return epochSystem.CopyAt(Time,i=>ephemeris.Position(i,Time),i=>ephemeris.Velocity(i,Time));}}
        public Vector3D PlanetPosition(int index){return ephemeris.Position(index,Time)-ephemeris.Position(0,Time);}
        public MarsCruise(Flight parking,MarsTransferPlan plan,Vector3D delta,double dryMass,double fuel,double isp){
            var start=parking.System.Copy();
            epochSystem=start;
            double span=(plan==null?260*Day:plan.ArrivalTime-start.Time)+40*Day;
            limitTime=start.Time+Math.Max(12*Day,span);
            ephemeris=new Ephemeris(start,limitTime-start.Time);
            spacecraft=new State(start.Time,start.Positions[Earth]+parking.Position,start.Velocities[Earth]+parking.Velocity);
            this.dryMass=Math.Max(1,dryMass);Fuel=Math.Max(0,fuel);this.isp=Math.Max(1,isp);
            BurnAccepted=ApplyCorrection(delta);
            Status=BurnAccepted?"Departure burn executed.":"Departure burn exceeds available propellant.";
        }
        public double RemainingDeltaV {get{return AvailableDeltaV(dryMass,Fuel,isp);}}
        public static double AvailableDeltaV(double dry,double fuel,double isp){
            return .00980665*isp*Math.Log((dry+fuel)/dry);
        }
        public static double FuelAfterBurn(double dry,double fuel,double isp,double deltaV){
            return (dry+fuel)*Math.Exp(-deltaV/(.00980665*isp))-dry;
        }
        public bool ApplyCorrection(Vector3D delta){
            if(EntryReached||Failed||double.IsNaN(delta.Length)||delta.Length>RemainingDeltaV+1e-9)return false;
            double remaining=FuelAfterBurn(dryMass,Fuel,isp,delta.Length);
            Fuel=Math.Max(0,remaining);spacecraft.Velocity+=delta;DeltaVUsed+=delta.Length;
            return true;
        }
        public MarsTransferPlan Predict(){
            var path=new List<Vector3D>();bool entered;double closest;
            var result=Encounter(ephemeris,spacecraft,limitTime,out entered,out closest,path);
            var p=result.Position-ephemeris.Position(Mars,result.Time);
            var v=result.Velocity-ephemeris.Velocity(Mars,result.Time)-MarsAtmosphereVelocity(p);
            return new MarsTransferPlan{BurnTime=Time,ArrivalTime=result.Time,FlightDays=(result.Time-Time)/Day,
                Path=path.ToArray(),ClosestAltitude=closest,EntrySpeed=entered?v.Length:0,
                EntryFlightPathAngle=entered?RelativeFlightAngle(p,v):0,
                EstimatedLandingErrorKm=entered?LandingError(ephemeris,result):double.NaN};
        }
        public void Tick(double seconds){
            if(!BurnAccepted||EntryReached||Failed||!(seconds>0))return;
            queued=Math.Min(queued+seconds,limitTime-spacecraft.Time);
            while(queued>0){
                double step=Math.Min(limitTime-spacecraft.Time,SpeedStep(ephemeris,spacecraft));
                if(queued<step-1e-7)break;
                var previous=spacecraft;
                spacecraft=PropagateStep(ephemeris,spacecraft,step);
                queued-=step;
                if(CrossesInterface(ephemeris,spacecraft)){
                    spacecraft=RefineEntry(ephemeris,previous,spacecraft);
                    EntryReached=true;Status="Mars atmospheric interface reached.";return;
                }
                if(EarthRange<Body.Earth.Radius){
                    Failed=true;Status="Earth impact. Departure did not clear the planet.";return;
                }
                if(Time>=limitTime-1e-7){
                    Failed=true;Status="No atmospheric intercept during this transfer.";return;
                }
            }
        }
    }
}

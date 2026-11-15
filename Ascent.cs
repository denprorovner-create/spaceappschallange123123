using System;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed class Propulsion {
        public readonly string Name, Info;
        public readonly double Dry, Fuel, Thrust, Isp, Cost;
        public Propulsion(string name,string info,double dry,double fuel,double thrust,double isp,double cost){
            Name=name;Info=info;Dry=dry;Fuel=fuel;Thrust=thrust;Isp=isp;Cost=cost;
        }
    }

    sealed class StackDesign {
        public const double CoreTankCost=4, UpperTankCost=3;
        public const double FairingMass=85;
        public static readonly Propulsion[] Cores={
            new Propulsion("Kestrel","LOX / kerosene",3050,50500,1120000,319,10),
            new Propulsion("Vulcan","LOX / methane",4050,62000,1610000,346,22),
            new Propulsion("Aurora","LOX / hydrogen",4800,73000,1700000,397,36)
        };
        public static readonly Propulsion[] Upper={
            new Propulsion("Needle","Vacuum kerosene",580,9400,125000,355,5),
            new Propulsion("Sparrow","Vacuum methane",740,13500,190000,376,11),
            new Propulsion("Aster","Vacuum hydrogen",980,17400,238000,448,17)
        };
        public static readonly Propulsion[] Boosters={
            new Propulsion("None","Clean core",0,0,0,0,0),
            new Propulsion("Twin solids","Fixed thrust after ignition",2400,13000,600000,259,5),
            new Propulsion("Twin liquids","Throttleable kerosene",1900,14000,480000,317,8)
        };
        public int Core, Booster, Engine;
        public int CoreTanks=1, UpperTanks=1, BoosterPairs=1;
        public bool Ready { get { return Core>=0 && Engine>=0 && CoreTanks>0 && UpperTanks>0; } }
        public double Load=1;
        public Propulsion Main {get{return Cores[Math.Max(0,Core)];}}
        public Propulsion Vacuum {get{return Upper[Math.Max(0,Engine)];}}
        public Propulsion Assist {get{return Boosters[Booster];}}
        public double Cost {get{return (Core>=0?Main.Cost:0)+(Engine>=0?Vacuum.Cost:0)+Assist.Cost*BoosterPairs+CoreTankCost*CoreTanks+UpperTankCost*UpperTanks;}}
        public double CoreDry { get { return (Core>=0?Main.Dry*.70:0)+Main.Dry*.30*CoreTanks; } }
        public double UpperDry { get { return (Engine>=0?Vacuum.Dry*.62:0)+Vacuum.Dry*.38*UpperTanks; } }
        public double CoreLoad { get { return Main.Fuel*Load*CoreTanks; } }
        public double UpperLoad { get { return Vacuum.Fuel*Load*UpperTanks; } }
        public double AssistDry { get { return Assist.Dry*BoosterPairs; } }
        public double AssistLoad { get { return Assist.Fuel*BoosterPairs; } }
        public double AssistThrust { get { return Assist.Thrust*BoosterPairs; } }
        public double LaunchMass(double payload){return Math.Max(.1,CoreDry+CoreLoad+UpperDry+UpperLoad+AssistDry+AssistLoad+payload+(payload>0?FairingMass:0));}
        public double PadTwr(double payload,Body body){return ((Core>=0&&CoreLoad>0?Main.Thrust:0)+AssistThrust)/(LaunchMass(payload)*body.GM/(body.Radius*body.Radius)*1000);}
        public double IdealDv(double payload){
            if(!Ready)return 0;
            double top=payload+UpperDry, core=top+UpperLoad+CoreDry;
            double fairing=payload>0?FairingMass:0;
            double full=LaunchMass(payload), coreFuel=CoreLoad, assistFuel=AssistLoad;
            double dv=0;
            if(assistFuel>0){
                double coreFlow=Main.Thrust/(9.80665*Main.Isp), assistFlow=AssistThrust/(9.80665*Assist.Isp);
                double overlap=Math.Min(coreFuel/coreFlow,assistFuel/assistFlow);
                double used=(coreFlow+assistFlow)*overlap;
                dv+=(Main.Thrust+AssistThrust)/(coreFlow+assistFlow)*Math.Log(full/(full-used))/1000;
                coreFuel-=coreFlow*overlap;assistFuel-=assistFlow*overlap;
                if(assistFuel>.001)dv+=.00980665*Assist.Isp*Math.Log((full-used)/(full-used-assistFuel));
            }
            // Estimate with the fairing still fitted during the core burn and
            // gone before the vacuum burn. Actual jettison timing is player-controlled.
            return dv+.00980665*(Main.Isp*Math.Log((core+fairing+coreFuel)/(core+fairing))+Vacuum.Isp*Math.Log((top+UpperLoad)/top));
        }
        public StackDesign Copy(){return (StackDesign)MemberwiseClone();}
    }

    struct AscentSample {
        public readonly double Time, Altitude;
        public readonly int Stage;
        public AscentSample(double time,double altitude,int stage){Time=time;Altitude=altitude;Stage=stage;}
    }

    sealed class SeparationEvent {
        public readonly double Time, Pitch, Heading, DynamicPressure;
        public readonly int Stage;
        public SeparationEvent(Ascent launch){
            Time=launch.Time;
            Pitch=launch.Pitch;
            Heading=launch.HeadingOffset;
            DynamicPressure=launch.DynamicPressure;
            Stage=launch.Stage;
        }
    }

    sealed class Ascent {
        const double G0=9.80665;
        public readonly StackDesign Design;
        public readonly Mission Mission;
        public readonly SolarSystem System;
        public readonly double Payload;
        readonly Orbit plane;
        readonly double duration;
        readonly double initialAngle=-.22;
        public double CoreFuel, UpperFuel, BoosterFuel, Time, Throttle=1, Pitch=90, HeadingOffset;
        public double Countdown=4, Acceleration, DynamicPressure;
        public double SuggestedPitch=90, SuggestedThrottle=1;
        public bool Started, Failed, InOrbit, HasLifted, Auto=true, AutoStage=true, BoostersAttached;
        public bool FairingAttached {get;private set;}
        public bool CanSeparate {get{return Started&&Countdown<=0&&!Failed&&!InOrbit;}}
        public SeparationEvent FairingEvent, BoosterEvent, CoreEvent, PayloadEvent;
        public int PayloadFromStage {get{return PayloadEvent==null?Stage:PayloadEvent.Stage;}}
        public int SeparationRevision {get;private set;}
        public double DeploymentViewSeconds;
        public string Status="Pad systems ready.";
        public int Stage=0;
        public Vector3D Position, Velocity, Nose;
        public readonly System.Collections.Generic.List<AscentSample> Track=new System.Collections.Generic.List<AscentSample>();
        public double Altitude {get{return Position.Length-Mission.Body.Radius;}}
        public Vector3D LaunchRadial {get{return Basis(initialAngle);}}
        public Vector3D LaunchForward {get{return Basis(initialAngle+Math.PI/2);}}
        public Vector3D SurfaceVelocity {get{return Velocity-RotationalVelocity(Position);}}
        public double TotalMass {
            get {return Payload+(Stage==0?Design.CoreDry+CoreFuel:0)+(Stage<2?Design.UpperDry+UpperFuel:0)+(BoostersAttached?Design.AssistDry+BoosterFuel:0)+(FairingAttached?StackDesign.FairingMass:0);}
        }
        public Ascent(Mission mission,StackDesign design,double payload,SolarSystem system){
            Mission=mission; Design=design.Copy();Payload=payload;System=system;
            plane=Flight.Parking(mission);
            duration=mission.Body.Lunar?Math.Max(230,195+mission.Pe*.34):Math.Max(465,445+.085*mission.Pe);
            CoreFuel=design.CoreLoad;UpperFuel=design.UpperLoad;BoosterFuel=design.AssistLoad;
            BoostersAttached=design.Booster!=0;
            FairingAttached=payload>0;
            Nose=LaunchRadial;
            Track.Add(new AscentSample(0,0,0));
            Position=Nose*mission.Body.Radius;
            Velocity=RotationalVelocity(Position);
        }
        Vector3D Basis(double angle){return plane.Rotate(Math.Cos(angle),Math.Sin(angle));}
        static double Limit(double n,double low,double high){return Math.Max(low,Math.Min(high,n));}
        public void Start(){
            if(Started||Failed||InOrbit)return;
            Started=true;Status="Launch sequencer running.";
        }
        public void Separate(){
            if(!CanSeparate)return;
            if(BoostersAttached){
                BoosterEvent=new SeparationEvent(this);SeparationRevision++;
                BoostersAttached=false;Status=BoosterFuel>.02?"Boosters jettisoned with propellant remaining.":"Booster separation confirmed.";return;
            }
            if(Stage==0){
                CoreEvent=new SeparationEvent(this);SeparationRevision++;
                BoostersAttached=false;Stage=1;Status="Core separated. Vacuum engine ignition.";
            }else if(Stage==1){
                Throttle=0;ReleasePayload();
            }
        }
        public void JettisonFairing(){
            if(!CanSeparate||!FairingAttached)return;
            FairingEvent=new SeparationEvent(this);SeparationRevision++;
            FairingAttached=false;
            Status=DynamicPressure>2200?"Fairing jettisoned under aerodynamic load. Payload exposed.":"Fairing halves away. Spacecraft exposed.";
        }
        public bool AutoJettisonReady {
            get {return Mission.Body.Lunar?Altitude>=3:Altitude>=82&&DynamicPressure<1000;}
        }
        double Available {get{return (Stage==0?(Design.Core>=0&&CoreFuel>0?Design.Main.Thrust:0):(Design.Engine>=0&&UpperFuel>0?Design.Vacuum.Thrust:0))+(BoostersAttached&&BoosterFuel>1e-5?Design.AssistThrust:0);}}
        Vector3D RotationalVelocity(Vector3D position){
            return Mission.Body.SurfaceVelocity(position);
        }
        Vector3D PadPosition(){
            var initial=LaunchRadial*Mission.Body.Radius;
            double angle=Orbit.Circle*Time/Mission.Body.Day;
            double c=Math.Cos(angle),s=Math.Sin(angle);
            return new Vector3D(initial.X*c-initial.Z*s,initial.Y,initial.X*s+initial.Z*c);
        }
        Vector3D GravityAndDrag(Vector3D position,Vector3D velocity,double t){
            Vector3D a=System.RelativeGravity(Mission.Body,position,t);
            double h=position.Length-Mission.Body.Radius;
            if(!Mission.Body.Lunar&&h<140){
                var air=velocity-RotationalVelocity(position);
                double rho=1.225*Math.Exp(-Math.Max(0,h)/7.3);
                DynamicPressure=.5*rho*air.LengthSquared*1e6;
                double dragArea=Stage==0?3.8:1.7;
                if(!FairingAttached&&Payload>0)dragArea+=2.6;
                a-=air*(DynamicPressure*dragArea/(1000*Math.Max(.1,TotalMass)*Math.Max(.0001,air.Length)));
            }else DynamicPressure=0;
            return a;
        }
        Vector3D Guidance(Vector3D radial,Vector3D tangent,Vector3D natural){
            double u=Limit(Time/duration,0,1), h=Mission.Pe;
            double href=h*u*u*u*(10+u*(-15+6*u)), vr=30*h*u*u*(1-u)*(1-u)/duration;
            double ar=60*h*u*(1-u)*(1-2*u)/(duration*duration);
            double final=Math.Sqrt(Mission.Body.GM/(Mission.Body.Radius+h));
            double start=Orbit.Circle*Mission.Body.Radius/Mission.Body.Day*Math.Cos(plane.Inc);
            double speed=start+(final-start)*Math.Pow(u,1.33);
            double drive=1.33*(final-start)*Math.Pow(u,.33)/duration;
            if(!Mission.Body.Lunar && Time<65) {
                speed=Math.Min(speed,.27+.0075*Time);
                drive=Math.Min(drive,.0075);
            }
            double radialV=Vector3D.DotProduct(Velocity,radial), speedV=Vector3D.DotProduct(Velocity,tangent);
            double altitude=Altitude;
            if(u>=1){ar=0;drive=0;}
            double r=ar-speedV*speedV/Position.Length+.032*(vr-radialV)+.0006*(href-altitude);
            double s=drive+speedV*radialV/Position.Length+.024*(speed-speedV);
            var normal=Vector3D.CrossProduct(radial,tangent); normal.Normalize();
            double planeError=Vector3D.DotProduct(Position,Vector3D.CrossProduct(Basis(0),Basis(Math.PI/2)));
            double vNormal=Vector3D.DotProduct(Velocity,normal);
            double planeCorrection=Mission.Body.Lunar?1:Limit((altitude-10)/15,0,1);
            return radial*r+tangent*s+normal*((-.06*vNormal-.002*planeError)*planeCorrection)-natural;
        }
        public void ReleaseNow(){
            if(Started&&Countdown<=0&&!Failed&&!InOrbit)ReleasePayload();
        }
        void ReleasePayload(){
            if(FairingAttached)JettisonFairing();
            if(BoostersAttached){
                BoosterEvent=new SeparationEvent(this);
                BoostersAttached=false;
            }
            PayloadEvent=new SeparationEvent(this);SeparationRevision++;
            Throttle=0;Acceleration=0;
            Stage=2;InOrbit=true;Status="Payload separated.";
        }
        void TryOrbit(){
            var o=Orbit.FromState(Mission.Body,Position,Velocity,Time);
            if(o==null||o.Pe<(Mission.Body.Lunar?10:115)){
                return;
            }
            if(Math.Abs(o.Pe-Mission.Pe)>14 || Math.Abs(o.Ap-Mission.Pe)>14)return;
            ReleasePayload();
        }
        public void Tick(double seconds,bool stopAfterSeparation=false){
            if(!Started||Failed||InOrbit)return;
            if(Countdown>0){
                var passed=Math.Min(seconds,Countdown);Countdown-=passed;seconds-=passed;
                if(Countdown<=0)Status="Engines commanded.";
            }
            int revision=SeparationRevision;
            while(seconds>1e-7&&!Failed&&!InOrbit){
                double dt=Math.Min(.20,seconds);
                Step(dt);seconds-=dt;
                if(stopAfterSeparation&&revision!=SeparationRevision)break;
            }
        }
        void Step(double dt){
            if(AutoStage){
                if(FairingAttached&&AutoJettisonReady)JettisonFairing();
                if(Mission.Body.Lunar && Stage==0 && Time>duration*.53 && Altitude>8 && !BoostersAttached)Separate();
                if(BoostersAttached&&BoosterFuel<.02)Separate();
                if(Stage==0&&CoreFuel<.02&&!BoostersAttached)Separate();
            }
            Vector3D radial=Position;radial.Normalize();
            var normal=Vector3D.CrossProduct(Basis(0),Basis(Math.PI/2));normal.Normalize();
            var tangent=Vector3D.CrossProduct(normal,radial);tangent.Normalize();
            var natural=GravityAndDrag(Position,Velocity,Time);
            var need=Guidance(radial,tangent,natural);
            SuggestedPitch=Time<8?90:Math.Atan2(Vector3D.DotProduct(need,radial),Vector3D.DotProduct(need,tangent))*180/Math.PI;
            SuggestedThrottle=Available>0?Limit(need.Length*1000*TotalMass/Available,0,1):0;
            if(Time<8)SuggestedThrottle=Math.Max(SuggestedThrottle,.87);
            if(Auto){Pitch=SuggestedPitch;Throttle=SuggestedThrottle;HeadingOffset=0;}
            double basePitch=Pitch*Math.PI/180, heading=HeadingOffset*Math.PI/180;
            var horizontal=tangent*Math.Cos(heading)+normal*Math.Sin(heading);
            Nose=radial*Math.Sin(basePitch)+horizontal*Math.Cos(basePitch);
            if(Auto && Time>=8){if(need.Length>1e-12){need.Normalize();Nose=need;}}
            var motor=Stage==0?Design.Main:Design.Vacuum;
            double fuel=Stage==0?CoreFuel:UpperFuel;
            double boosterCommand=Design.Booster==1?1:Throttle;
            double boosterForce=BoostersAttached?Draw(Design.Assist,boosterCommand*Design.BoosterPairs,dt,ref BoosterFuel):0;
            double mass=Math.Max(.1,TotalMass);
            bool motorInstalled=Stage==0?Design.Core>=0:Design.Engine>=0;
            double force=(motorInstalled?Draw(motor,Throttle,dt,ref fuel):0)+boosterForce;
            if(Stage==0)CoreFuel=fuel;else UpperFuel=fuel;
            Acceleration=force/mass*Mission.Body.Radius*Mission.Body.Radius/(Mission.Body.GM*1000);
            var a=natural+Nose*(force/(1000*mass));
            Position+=Velocity*dt+a*(dt*dt/2);
            var later=GravityAndDrag(Position,Velocity+a*dt,Time+dt)+Nose*(force/(1000*mass));
            Velocity+=(a+later)*(dt*.5);
            Time+=dt; System.Advance(dt);
            if(Time-Track[Track.Count-1].Time>=1)Track.Add(new AscentSample(Time,Altitude,Stage));
            if(!HasLifted && Altitude<=.0001){
                Position=PadPosition();
                Velocity=RotationalVelocity(Position);
                Status=force==0?"On pad · no thrust.":"On pad · insufficient vertical thrust.";
                return;
            }
            if(!HasLifted)Status="Liftoff confirmed.";
            HasLifted=true;
            if(Altitude<-.001){Failed=true;Status="Vehicle lost on impact.";}
            if(Time>2 && DynamicPressure>155000){Failed=true;Status="Vehicle lost at excessive dynamic pressure. Reduce throttle in dense air.";}
            else if(Payload>0&&!FairingAttached&&DynamicPressure>60000){
                Failed=true;
                Status="Exposed payload failed under aerodynamic load. Keep the fairing on through dense air.";
            }
            if(Auto && AutoStage && Time>=duration && Stage==1)TryOrbit();

        }
        static double Draw(Propulsion p,double throttle,double dt,ref double fuel){
            if(p.Thrust==0 || fuel<=0)return 0;
            double burn=Math.Min(fuel,p.Thrust*throttle*dt/(G0*p.Isp));
            fuel-=burn;return burn*G0*p.Isp/dt;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed class Body {
        public readonly string Name, Texture;
        public readonly double Radius, GM, Day;
        public bool Lunar { get { return Name == "Moon"; } }
        public Body(string name, string texture, double radius, double gm, double day) {
            Name=name; Texture=texture; Radius=radius; GM=gm; Day=day;
        }
        public Vector3D SurfaceVelocity(Vector3D position) {
            return Vector3D.CrossProduct(new Vector3D(0,-Orbit.Circle/Day,0),position);
        }
        public static readonly Body Earth=new Body("Earth","earth.jpg",6378.1366,398600.435507,86164.09054);
        public static readonly Body Moon=new Body("Moon","moon.jpg",1737.4,4902.800118,27.321661*86400);
    }

    sealed class Orbit {
        public const double Circle=2*Math.PI;
        public readonly Body Body;
        public readonly double A, E, Inc, Node, Arg, Epoch, Start, Period;
        public double Pe { get { return A*(1-E)-Body.Radius; } }
        public double Ap { get { return A*(1+E)-Body.Radius; } }
        public Orbit(Body body, double a, double e, double inc, double node, double arg, double epoch, double start) {
            Body=body; A=a; E=e; Inc=inc; Node=node; Arg=arg; Epoch=epoch; Start=start;
            Period=Circle*Math.Sqrt(a*a*a/body.GM);
        }
        public static Orbit Plan(Body body, double pe, double ap, double inclination, double node, double arg) {
            double a=body.Radius+(pe+ap)/2;
            return new Orbit(body,a,(ap-pe)/(2*a),inclination*Math.PI/180,node*Math.PI/180,arg*Math.PI/180,0,0);
        }
        public static double Wrap(double n) { return ((n%Circle)+Circle)%Circle; }
        public Vector3D Rotate(double x, double z) {
            double u=x*Math.Cos(Arg)-z*Math.Sin(Arg), v=x*Math.Sin(Arg)+z*Math.Cos(Arg);
            return new Vector3D(u*Math.Cos(Node)-v*Math.Cos(Inc)*Math.Sin(Node), v*Math.Sin(Inc),
                u*Math.Sin(Node)+v*Math.Cos(Inc)*Math.Cos(Node));
        }
        public double Mean(double t) { return Wrap(Start+(t-Epoch)*Circle/Period); }
        public double Eccentric(double t) {
            double m=Mean(t), anomaly=E<.8?m:Math.PI;
            for(int i=0;i<24;i++){
                double d=(anomaly-E*Math.Sin(anomaly)-m)/(1-E*Math.Cos(anomaly));
                anomaly-=d; if(Math.Abs(d)<1e-12)break;
            }
            return anomaly;
        }
        public Vector3D AtAngle(double anomaly) {
            return Rotate(A*(Math.Cos(anomaly)-E), A*Math.Sqrt(1-E*E)*Math.Sin(anomaly));
        }
        public Vector3D Position(double t) { return AtAngle(Eccentric(t)); }
        public Vector3D Velocity(double t) {
            double anomaly=Eccentric(t);
            double rate=Circle/Period/(1-E*Math.Cos(anomaly));
            return Rotate(-A*Math.Sin(anomaly)*rate,A*Math.Sqrt(1-E*E)*Math.Cos(anomaly)*rate);
        }
        public double TimeToPeri(double t) {
            double m=Mean(t);
            return m<1e-9||Circle-m<1e-9?0:(Circle-m)*Period/Circle;
        }
        public static Orbit FromState(Body b, Vector3D r, Vector3D v, double time) {
            double radius=r.Length;
            if(!(radius>0))return null;
            double energy=v.LengthSquared/2-b.GM/radius;
            if(!(energy<0))return null;
            Vector3D h=Vector3D.CrossProduct(v,r);
            if(h.Length<1e-8)return null;
            h.Normalize();
            Vector3D node=Vector3D.CrossProduct(h,new Vector3D(0,1,0));
            double longitude=node.Length<1e-8?0:Math.Atan2(node.Z,node.X);
            double inc=Math.Acos(Math.Max(-1,Math.Min(1,h.Y)));
            Vector3D p=new Vector3D(Math.Cos(longitude),0,Math.Sin(longitude));
            Vector3D q=Vector3D.CrossProduct(p,h);
            double a=-b.GM/(2*energy);
            Vector3D ev=(r*(v.LengthSquared-b.GM/radius)-v*Vector3D.DotProduct(r,v))/b.GM;
            double e=ev.Length;
            if(!(e<1))return null;
            double arg=e<1e-8?0:Math.Atan2(Vector3D.DotProduct(ev,q),Vector3D.DotProduct(ev,p));
            Vector3D axis=p*Math.Cos(arg)+q*Math.Sin(arg), side=-p*Math.Sin(arg)+q*Math.Cos(arg);
            double anomaly=Math.Atan2(Vector3D.DotProduct(r,side)/(a*Math.Sqrt(1-e*e)),Vector3D.DotProduct(r,axis)/a+e);
            return new Orbit(b,a,e,inc,longitude,arg,time,Wrap(anomaly-e*Math.Sin(anomaly)));
        }
    }

    sealed class Mission {
        public string Name, Objective, Experiment;
        public Body Body;
        public double Pe, Ap, Inclination, Node, Arg, Hours, Dose, Budget;
        public int Instrument;
        public Orbit Target { get { return Orbit.Plan(Body,Pe,Ap,Inclination,Node,Arg); } }
        public Mission(string name,Body body,double pe,double ap,double inc,double node,double arg,double hours,double dose,double budget,int instrument,string objective,string experiment) {
            Name=name;Body=body;Pe=pe;Ap=ap;Inclination=inc;Node=node;Arg=arg;Hours=hours;Dose=dose;Budget=budget;
            Instrument=instrument;Objective=objective;Experiment=experiment;
        }
        static readonly Body E=Body.Earth, M=Body.Moon;
        public static readonly Mission[] Archive={
            new Mission("Blue Marble",E,360,550,51.6,20,25,24,.40,82,0,"Survey river deltas and coastal plumes.","Acquire daylight multispectral strips."),
            new Mission("Polar Watch",E,420,760,97.4,43,0,30,.50,88,0,"Survey ice margins across high latitudes.","Acquire daylight images above 60° latitude."),
            new Mission("Ember Lines",E,480,630,37,115,50,24,.43,89,1,"Trace active fire scars and heat anomalies.","Collect infrared radiance measurements."),
            new Mission("Night Harbor",E,380,620,29,55,152,24,.46,91,2,"Map harbor approaches through cloud and darkness.","Collect synthetic-aperture radar swaths."),
            new Mission("Equatorial Relay",E,250,4200,3,330,25,36,.55,104,3,"Relay packets from equatorial terminals.","Record store-and-forward communications packets."),
            new Mission("Auroral Arc",E,500,2750,78,295,88,36,.38,102,4,"Sample high-latitude magnetic field changes.","Record magnetometer data above 60° latitude."),
            new Mission("Long Baseline",E,700,14400,63.4,175,270,48,.59,110,3,"Characterize latency over a wide range of slant distances.","Record relay packets near apogee."),
            new Mission("Mineral Atlas",M,75,120,83,90,18,30,.42,115,1,"Identify mineral signatures across lunar highlands.","Collect infrared spectral observations on the sunlit side."),
            new Mission("Mare Transect",M,60,155,32,15,8,24,.44,112,0,"Map lava plains and crater ejecta boundaries.","Acquire multispectral imagery on the sunlit side."),
            new Mission("Dark Ice",M,90,210,91,210,45,40,.46,126,2,"Probe polar terrain with radar.","Collect radar swaths above 60° latitude."),
            new Mission("Far Side Relay",M,250,3400,61,130,180,42,.59,133,3,"Carry far-side surface packets into Earth view.","Record packets on the lunar far side and forward them on the near side."),
            new Mission("Magnetic Fossils",M,80,260,42,100,72,30,.41,119,4,"Measure localized magnetic anomalies close to the Moon.","Record magnetometer data below 180 km."),
            new Mission("Terminator Chase",M,100,450,74,60,35,36,.40,122,0,"Capture terrain where long shadows reveal relief.","Acquire imagery near the day-night boundary."),
            new Mission("Crater Echo",M,110,780,49,260,130,38,.62,132,2,"Profile highland craters using active radar.","Collect radar swaths below 500 km."),
            new Mission("Lunar Pathfinder",M,140,5800,65,185,270,54,.68,142,4,"Survey the field along a long eccentric lunar orbit.","Collect magnetometer transects near apoapsis.")
        };
        public bool Observable(Vector3D p) {
            double x=p.X/p.Length, lat=Math.Abs(p.Y)/p.Length, alt=p.Length-Body.Radius;
            switch(Name){
                case "Polar Watch": return lat>.866 && x>0;
                case "Auroral Arc": case "Dark Ice": return lat>.866;
                case "Long Baseline": case "Lunar Pathfinder": return alt>(Ap+Pe)/2;
                case "Far Side Relay": return x<-.20;
                case "Magnetic Fossils": return alt<180;
                case "Terminator Chase": return x>0 && x<.37;
                case "Crater Echo": return alt<500;
                default:return Instrument>1 || x>0;
            }
        }
    }

    sealed class Part {
        public readonly string Name, Detail;
        public readonly double Mass, Cost, Power, A, B;
        public Part(string name,string detail,double mass,double cost,double power,double a,double b=0) {
            Name=name;Detail=detail;Mass=mass;Cost=cost;Power=power;A=a;B=b;
        }
    }

    sealed class Vehicle {
        public const double OrbitalThrustN=2000;
        public static readonly string[] Slots={"Core engine","Avionics","Propellant","Solar / battery","Antenna","Instrument","Ground network"};
        public static readonly Part[][] Catalog={
            new Part[0],
            new[]{new Part("Kite bus","Compact • 1.8 GB",125,9,40,1.8),new Part("Survey bus","Redundant avionics • 4.5 GB",215,15,52,4.5),new Part("Deep space bus","Radiation hardened • 9 GB",365,26,72,9)},
            new[]{new Part("Compact tank","80 kg propellant • 2 kN • 315 s Isp",29,4,0,80,315),new Part("Transfer tank","240 kg propellant • 2 kN • 335 s Isp",55,8,0,240,335),new Part("Extended tank","500 kg propellant • 2 kN • 355 s Isp",87,14,0,500,355),new Part("Cislunar propulsion","2,400 kg propellant • 2 kN • 448 s Isp",150,24,0,2400,448)},
            new[]{new Part("Body panels","190 W • 360 Wh battery",32,5,190,360),new Part("Twin solar wings","420 W • 900 Wh battery",70,9,420,900),new Part("Quad solar wings","680 W • 1750 Wh battery",125,16,680,1750)},
            new[]{new Part("S-band patch","Low mass • 0.24 GB/h",15,4,35,.24),new Part("X-band dish","Directional • 0.8 GB/h",40,8,75,.8),new Part("Ka-band array","High rate • 1.7 GB/h",82,15,145,1.7)},
            new[]{new Part("Multispectral imager","Visible mapping • 0.60 GB/h",55,9,73,.60),new Part("IR spectrometer","Thermal / mineral spectra • 0.76 GB/h",68,11,111,.76),new Part("Radar sounder","Active radar • 0.96 GB/h",122,15,211,.96),new Part("Relay transponder","Surface packets • 1.1 GB/h",41,8,78,1.10),new Part("Magnetometer","Magnetic field • 0.53 GB/h",22,7,40,.53)},
            new[]{new Part("Regional station","One Earth longitude",0,3,0,1),new Part("Tracking network","Three longitudes",0,6,0,3),new Part("Global network","Six sites and extended lunar sessions",0,11,0,6)}
        };
        public StackDesign Stack=new StackDesign();
        public readonly int[] Selections={0,1,1,1,1,0,1};
        public readonly bool[] ExtraInstruments=new bool[5];
        static readonly Part Empty=new Part("Empty","",0,0,0,0);
        public bool Has(int slot) { return Selections[slot]>=0; }
        public Part this[int slot] { get { return Has(slot)?Catalog[slot][Selections[slot]]:Empty; } }
        public bool HasInstrument(int instrument) { return instrument>=0 && instrument<5 && (Selections[5]==instrument||ExtraInstruments[instrument]); }
        public IEnumerable<int> InstalledInstruments { get { return Enumerable.Range(0,5).Where(HasInstrument); } }
        public double InstrumentMass { get { return InstalledInstruments.Sum(i=>Catalog[5][i].Mass); } }
        public double InstrumentCost { get { return InstalledInstruments.Sum(i=>Catalog[5][i].Cost); } }
        public double InstrumentPower { get { return InstalledInstruments.Sum(i=>Catalog[5][i].Power); } }
        public double InstrumentRate { get { return InstalledInstruments.Sum(i=>Catalog[5][i].A); } }
        public Vehicle Copy() { var v=new Vehicle(); Array.Copy(Selections,v.Selections,7); Array.Copy(ExtraInstruments,v.ExtraInstruments,5); v.Stack=Stack.Copy(); return v; }
        public double Dry { get { return Enumerable.Range(1,4).Sum(i=>this[i].Mass)+InstrumentMass; } }
        public double Cost { get { return Stack.Cost+Enumerable.Range(1,4).Sum(i=>this[i].Cost)+InstrumentCost+this[6].Cost; } }
        public double Wet { get { return Dry+this[2].A; } }
        public double FuelDV(double fuel) { return fuel<=0?0:.00980665*this[2].B*Math.Log((Dry+fuel)/Math.Max(.001,Dry)); }
        public List<string> Issues(Mission m) {
            var issues=new List<string>();
            if(Cost>m.Budget)issues.Add((Cost-m.Budget).ToString("0")+" M over the mission budget; flight remains available.");
            if(!Stack.Ready)issues.Add("An empty engine or tank mount leaves that stage without thrust.");
            if(Stack.PadTwr(Wet,Body.Earth)<1.10)issues.Add("Low thrust-to-weight: more thrust or less mass improves liftoff.");
            if(Stack.IdealDv(Wet)<8.3)issues.Add("Low ascent Δv: added propellant helps range but also adds launch mass.");
            if(this[3].Power<this[1].Power+this[4].Power+InstrumentPower)issues.Add("All systems together draw more than the panels supply. Schedule experiments and downlinks separately.");
            if(this[3].A<this[1].Power*Math.Min(2,m.Target.Period/7200))issues.Add("The battery may run out during eclipse; a larger pack adds mass.");
            if(!HasInstrument(m.Instrument))issues.Add("The assigned experiment is not installed. Other science can still run.");
            if(!Has(1)||!Has(3))issues.Add("Science and radio need avionics and power.");
            if(!Has(4)||!Has(6))issues.Add("Downlink needs an antenna and ground network.");
            if(FuelDV(this[2].A)<Flight.PlannedDv(m)+(m.Body.Lunar?4.35:0))issues.Add("Orbital Δv is below the estimated transfer and insertion requirement.");
            return issues;
        }
    }

    sealed class Flight {
        const double Gravity0=9.80665;
        const double AtmosphereTopKm=150;
        const double PayloadPressureLimitPa=40000;
        const double PayloadDragAreaM2=2.0;
        public readonly Mission Mission;
        public readonly Vehicle Vehicle;
        public readonly Orbit Target;
        public Orbit Current;
        public Vector3D Position, Velocity;
        public readonly SolarSystem System;
        readonly double dryMass, exhaustSpeed;
        public double Time, Fuel, Charge, Stored, Returned, AtmospherePressure;
        public Body Reference {get;private set;}
        public Vector3D SurfaceVelocity { get { return Velocity-Reference.SurfaceVelocity(Position); } }
        public LunarTransfer TransferPlan {get;private set;}
        public bool AwaitingLunarTransfer {get{return Mission.Body.Lunar&&Reference==Body.Earth&&TransferPlan==null;}}
        public bool InLunarTransfer {get{return Reference==Body.Earth&&TransferPlan!=null;}}
        public double ScienceStart {get;private set;}
        public bool Finished, Success, Inserted, DeadlineMissed;
        public readonly bool[] ExperimentEnabled={true,true,true,true,true};
        public readonly string[] ExperimentStates={"Idle","Idle","Idle","Idle","Idle"};
        public readonly double[] CollectedByInstrument=new double[5],StoredByInstrument=new double[5],ReturnedByInstrument=new double[5];
        public readonly List<FlightSample> History=new List<FlightSample>();
        public readonly List<FlightEvent> Events=new List<FlightEvent>();
        public double GenerationW,DemandW,RequestedW,ScienceRate,DownlinkRate,LinkDistance;
        public double StartedAt,InitialFuel,MinimumCharge,ScienceSeconds,DownlinkSeconds,LinkBlockedSeconds;
        public double UnpoweredSeconds,RecorderFullSeconds,SunlitSeconds,OnTargetSeconds,DeltaVUsed,QualifiedCollected;
        public bool ReachedTarget;
        public string ScienceState="Idle",RadioState="Idle";
        double lastSample,sampleInterval=30;
        bool wasSunlit,wasContact,wasOnPlan,powerLimited,recorderLimited;
        readonly bool enforceBudget;
        public string Status="Parking orbit acquired. Warp to the maneuver node, then execute insertion.";
        public static Orbit Parking(Mission m) {
            return Orbit.Plan(m.Body,m.Pe,m.Pe,m.Inclination,m.Node,m.Arg);
        }
        public static double PlannedDv(Mission m) {
            return (m.Target.Velocity(0)-Parking(m).Velocity(0)).Length;
        }
        public Flight(Mission m,Vehicle vehicle,Ascent launch=null,bool enforceBudget=true) {
            Mission=m;Vehicle=vehicle.Copy();Target=m.Target;this.enforceBudget=enforceBudget;
            dryMass=Vehicle.Dry;exhaustSpeed=Gravity0*Vehicle[2].B;
            var p=Parking(m);
            Current=new Orbit(p.Body,p.A,p.E,p.Inc,p.Node,p.Arg,0,Orbit.Circle*.85);
            System=launch==null?new SolarSystem():launch.System;
            Time=System.Time;
            Reference=launch==null?m.Body:launch.Mission.Body;
            ScienceStart=Reference==m.Body?0:double.PositiveInfinity;
            Position=launch==null?Current.Position(0):launch.Position;
            Velocity=launch==null?Current.Velocity(0):launch.Velocity;
            UpdateOrbit();
            Fuel=Vehicle[2].A; Charge=Vehicle[3].A;
            if(launch!=null && (Current==null||Current.Pe<(m.Body.Lunar?0:AtmosphereTopKm)))
                Status="Payload deployed. Check the trajectory and burn if needed.";
            StartedAt=Time;InitialFuel=Fuel;MinimumCharge=Charge;
            ReachedTarget=wasOnPlan=OnPlan;wasSunlit=Sun(Position);wasContact=Contact(Position,Time);
            Log(launch==null?"Spacecraft initialized in parking orbit.":"Spacecraft deployed from launch vehicle.");
            if(AwaitingLunarTransfer)Status="Earth parking orbit. Plan the lunar departure in Maneuvers.";
            lastSample=Time;History.Add(new FlightSample(this));
        }
        bool engineFiring;
        public bool EngineFiring {
            get { return engineFiring&&Fuel>0; }
            set { engineFiring=value&&Fuel>0&&exhaustSpeed>0&&!Finished; }
        }
        public int Attitude=1;
        public Vector3D BurnDirection {
            get {
                Vector3D direction;
                switch(Math.Abs(Attitude)){
                    case 2: direction=Position;break;
                    case 3: direction=Vector3D.CrossProduct(Velocity,Position);break;
                    default: direction=Velocity;break;
                }
                if(direction.LengthSquared<1e-16)return new Vector3D(0,1,0);
                direction.Normalize();
                return direction*Math.Sign(Attitude);
            }
        }
        public double Throttle, Collected, QualifiedReturned;
        double qualifiedStored;
        void ApplyThrust(double seconds) {
            if(!EngineFiring||Throttle<=0)return;
            double before=dryMass+Fuel;
            double used=Math.Min(Fuel,Vehicle.OrbitalThrustN*Math.Min(1,Throttle)*seconds/exhaustSpeed);
            Fuel-=used;
            double delta=exhaustSpeed*Math.Log(before/(dryMass+Fuel));
            Velocity+=BurnDirection*(delta/1000);
            DeltaVUsed+=delta;
            if(Fuel<=0){
                EngineFiring=false;
                Log("Maneuver engine cut off: propellant exhausted.");
            }
        }
        public bool OnPlan {
            get {
                if(Current==null||Reference!=Mission.Body)return false;
                if(Math.Abs(Current.Pe-Target.Pe)>15 || Math.Abs(Current.Ap-Target.Ap)>Math.Max(18,Mission.Ap*.012))return false;
                if(Math.Abs(Current.Inc-Target.Inc)>.00873)return false;
                if(Math.Abs(Math.Sin(Target.Inc))>.01 && Math.Abs(Math.Sin((Current.Node-Target.Node)/2))>.00436)return false;
                if(Target.E<.004)return true;
                return Vector3D.AngleBetween(Current.AtAngle(0),Target.AtAngle(0))<4;
            }
        }
        void UpdateOrbit() {
            var fit=Orbit.FromState(Reference,Position,Velocity,Time);
            Current=fit;
        }
        public double NodeTime {
            get {
                if(Current==null)return double.PositiveInfinity;
                var targetRadial=Target.Rotate(1,0);
                double x=Vector3D.DotProduct(targetRadial,Current.Rotate(1,0));
                double y=Vector3D.DotProduct(targetRadial,Current.Rotate(0,1));
                if(x*x+y*y<1e-8)return double.PositiveInfinity;
                double angle=Math.Atan2(y,x);
                double eccentric=2*Math.Atan2(Math.Sqrt(1-Current.E)*Math.Sin(angle/2),
                    Math.Sqrt(1+Current.E)*Math.Cos(angle/2));
                double mean=eccentric-Current.E*Math.Sin(eccentric);
                double wait=Orbit.Wrap(mean-Current.Mean(Time))*Current.Period/Orbit.Circle;
                return Math.Min(wait,Current.Period-wait)<1?0:wait;
            }
        }
        public void WarpNode() {
            if(Finished||Inserted||Current==null||Reference!=Mission.Body)return;
            double remaining=NodeTime;
            if(double.IsInfinity(remaining)||remaining>3*86400){
                Status="No upcoming insertion point within three days.";
                return;
            }
            double deadline=Time+Math.Min(3*86400,remaining+30);
            while(!Finished&&remaining>2&&Time<deadline){
                Tick(Math.Min(Math.Min(remaining*.85,180),deadline-Time),0);
                remaining=NodeTime;
            }
            if(!Finished)Status=remaining<=2?"At the maneuver node. Execute the insertion burn.":"Insertion point moved. Recheck the orbit.";
        }
        public void Insert() {
            if(Finished||Inserted||Reference!=Mission.Body)return;
            if(NodeTime>5) {
                Status="Outside the burn window. Warp to the maneuver node first.";
                return;
            }
            var normal=Vector3D.CrossProduct(Target.Rotate(1,0),Target.Rotate(0,1));
            var radial=Position;radial.Normalize();
            var tangent=Vector3D.CrossProduct(normal,radial);tangent.Normalize();
            double r=Position.Length, ra=Target.A*(1+Target.E);
            if(r>ra+16 || Math.Abs(r-Mission.Body.Radius-Mission.Pe)>18){
                Status="Parking orbit height is outside the insertion corridor.";return;
            }
            double a=(r+Math.Max(r,ra))/2;
            var planned=tangent*Math.Sqrt(Mission.Body.GM*(2/r-1/a));
            if(Burn(planned-Velocity)) {
                Inserted=true;
                Status=OnPlan?"Target trajectory acquired. Arm science and open the downlink.":"Insertion complete. Current orbit differs from the target.";
            }
        }
        public bool Burn(Vector3D delta) {
            if(Finished)return false;
            if(!Vehicle.Has(2)){Status="No orbital propulsion installed.";return false;}
            double needed=(dryMass+Fuel)*(1-Math.Exp(-1000*delta.Length/exhaustSpeed));
            if(needed>Fuel+1e-8) {Status="Insufficient propellant for that burn.";return false;}
            Fuel=Math.Max(0,Fuel-needed);Velocity+=delta;DeltaVUsed+=delta.Length*1000;UpdateOrbit();
            Log(string.Format("Maneuver: {0:0.0} m/s; {1:0.0} kg propellant used.",delta.Length*1000,needed));return true;
        }
        public bool Sun(Vector3D p) {
            return MissionEnvironment.Sunlit(Reference,p,MissionEnvironment.SunVector(System,Reference));
        }
        public bool Contact(Vector3D p,double seconds) {
            double distance;
            return MissionEnvironment.Contact(Vehicle,Reference,p,seconds,MissionEnvironment.EarthVector(System),out distance);
        }
        public bool ScienceWindow(Vector3D p,int instrument) {
            return Reference==Mission.Body&&MissionEnvironment.ScienceWindow(Mission,p,instrument,MissionEnvironment.SunVector(System,Mission.Body),MissionEnvironment.EarthVector(System));
        }
        public void DepartForMoon(){
            if(!PrepareLunarDeparture())return;
            CommitLunarDeparture(LunarTransfer.Calculate(System,Position,Target));
        }
        public bool PrepareLunarDeparture(){
            if(Finished||!AwaitingLunarTransfer||EngineFiring)return false;
            var wait=LunarTransfer.FirstDeparture(this);
            if(double.IsNaN(wait)){Status="First establish a 160–1,800 km Earth parking orbit.";return false;}
            Tick(wait,0);
            return !Finished;
        }
        public void CommitLunarDeparture(LunarTransfer solution){
            if(Finished||!AwaitingLunarTransfer||EngineFiring)return;
            if(solution==null){Status="No safe lunar transfer was found from this parking orbit. "+LunarTransfer.Failure;return;}
            if(Math.Abs(Time-solution.BurnTime)>1e-5){Status="Departure window moved. Recalculate the burn.";return;}
            double required=(solution.DepartureVelocity-Velocity).Length+solution.PredictedCapture+PlannedDv(Mission);
            if(Vehicle.FuelDV(Fuel)<required+.08)
                Log(string.Format("Transfer and science orbit require about {0:F2} km/s. The vehicle has {1:F2} km/s remaining.",
                    required,Vehicle.FuelDV(Fuel)));
            if(!Burn(solution.DepartureVelocity-Velocity))return;
            TransferPlan=solution;
            Status="Translunar injection complete. Coast to the capture point.";
            Log(Status);
        }
        public double LunarDistance {get{return (Position-(System.Positions[4]-System.Positions[3])).Length-Body.Moon.Radius;}}
        public void WarpLunarArrival(){
            if(Finished||!InLunarTransfer||EngineFiring)return;
            Tick(Math.Max(0,TransferPlan.ArrivalTime-Time),0);
        }
        public void CaptureMoon(){
            if(Finished||Reference!=Body.Earth||!Mission.Body.Lunar||EngineFiring)return;
            var r=Position-(System.Positions[4]-System.Positions[3]);
            double alt=r.Length-Body.Moon.Radius;
            if(alt<2||Math.Abs(alt-Mission.Pe)>14){Status="Capture when lunar altitude is near the planned periapsis.";return;}
            var relativeV=Velocity-(System.Velocities[4]-System.Velocities[3]);
            var normal=Vector3D.CrossProduct(Target.Rotate(1,0),Target.Rotate(0,1));
            var direction=Vector3D.CrossProduct(normal,r);direction.Normalize();
            if(!Burn(direction*Math.Sqrt(Body.Moon.GM/r.Length)-relativeV))return;
            Position=r;
            Velocity-=System.Velocities[4]-System.Velocities[3];
            Reference=Body.Moon;ScienceStart=Time;UpdateOrbit();
            Status="Lunar orbit captured. Set the assigned science orbit.";
            Log(Status);
        }
        void Log(string message) {
            Events.Add(new FlightEvent(Time,message));
            if(Events.Count>240)Events.RemoveAt(0);
        }
        void Sample() {
            if(Time-lastSample<sampleInterval)return;
            lastSample=Time;History.Add(new FlightSample(this));
            if(History.Count>2048){
                for(int i=History.Count-2;i>0;i-=2)History.RemoveAt(i);
                sampleInterval*=2;
            }
        }
        double Density(Vector3D position) {
            double height=position.Length-Reference.Radius;
            if(Reference.Lunar||height>=AtmosphereTopKm)return 0;
            return 1.225*Math.Exp(-Math.Max(0,height)/7.3);
        }
        void ApplyDrag(double seconds) {
            double density=Density(Position);
            if(density==0){AtmospherePressure=0;return;}
            var relative=SurfaceVelocity;
            double speed=relative.Length*1000;
            AtmospherePressure=.5*density*speed*speed;
            // Exact speed decay for quadratic drag at the sampled density.
            // This remains stable through reentry even when drag is strong.
            double mass=Math.Max(1,dryMass+Fuel);
            double ratio=1/(1+.5*density*PayloadDragAreaM2*speed*seconds/mass);
            Velocity-=relative*(1-ratio);
        }
        bool CheckPhysicalLoss(){
            bool impact=Position.Length<Reference.Radius-.001||(InLunarTransfer&&LunarDistance<-.001);
            if(!impact&&AtmospherePressure<=PayloadPressureLimitPa)return false;
            EngineFiring=false;
            Finished=true;
            Status=impact?"Vehicle lost on surface impact.":"Vehicle broke up under aerodynamic load.";
            Log(Status);History.Add(new FlightSample(this));
            return true;
        }
        Vector3D AdvanceDynamics(double step){
            ApplyDrag(step*.5);
            double initialPressure=AtmospherePressure;
            ApplyThrust(step*.5);
            var acceleration=System.RelativeGravity(Reference,Position,Time);
            var midpoint=Position+Velocity*(step*.5)+acceleration*(step*step*.125);
            Position+=Velocity*step+acceleration*(step*step*.5);
            System.Advance(step);
            Velocity+=(acceleration+System.RelativeGravity(Reference,Position,Time+step))*(step*.5);
            ApplyThrust(step*.5);
            ApplyDrag(step*.5);
            AtmospherePressure=Math.Max(initialPressure,AtmospherePressure);
            return midpoint;
        }
        public void Tick(double seconds,int mode) {
            if(Finished||!(seconds>1e-8)||double.IsInfinity(seconds))return;
            double end=Time+seconds;
            bool eligibleOrbit=OnPlan;
            if(eligibleOrbit)ReachedTarget=true;
            if(eligibleOrbit!=wasOnPlan){Log(eligibleOrbit?"Assigned orbit acquired.":"Trajectory moved outside the assigned orbit tolerance.");wasOnPlan=eligibleOrbit;}
            while(Time<end-1e-8) {
                bool inAir=Position.Length<Reference.Radius+AtmosphereTopKm&&!Reference.Lunar;
                double step=Math.Min(EngineFiring||inAir?.25:InLunarTransfer?LunarTransfer.CruiseStep:5,end-Time), dt=step/3600;
                double now=Time+step*.5;
                var p=AdvanceDynamics(step);
                Time+=step;
                if(CheckPhysicalLoss())break;
                var panel=Vehicle[3]; var antenna=Vehicle[4];
                bool light=Sun(p);
                double distance;
                bool contact=MissionEnvironment.Contact(Vehicle,Reference,p,now,MissionEnvironment.EarthVector(System),out distance);
                LinkDistance=distance;
                if(light!=wasSunlit){Log(light?"Sunlight acquired; solar generation restored.":"Entered eclipse; operating on stored battery energy.");wasSunlit=light;}
                if(contact!=wasContact){Log(contact?"Earth ground link acquired.":"Earth ground link lost; data remain in the recorder.");wasContact=contact;}
                if(light)SunlitSeconds+=step;
                if(eligibleOrbit)OnTargetSeconds+=step;
                bool bus=Vehicle.Has(1)&&Vehicle.Has(3);
                bool requestScience=(mode&1)!=0;
                bool send=bus&&(mode&2)!=0&&Stored>1e-8&&contact;
                bool full=Stored>=Vehicle[1].A-1e-8;
                var active=new bool[5];
                double scienceLoad=0,collectionRate=0;
                int requested=0;
                for(int i=0;i<5;i++){
                    if(!Vehicle.HasInstrument(i)){ExperimentStates[i]="Not installed";continue;}
                    if(!requestScience||!ExperimentEnabled[i]){ExperimentStates[i]="Standby";continue;}
                    requested++;
                    if(!bus){ExperimentStates[i]="No avionics or power system";continue;}
                    if(!ScienceWindow(p,i)){ExperimentStates[i]="Waiting for observation window";continue;}
                    if(full){ExperimentStates[i]="Recorder full";continue;}
                    active[i]=true;scienceLoad+=Vehicle.Catalog[5][i].Power;collectionRate+=Vehicle.Catalog[5][i].A;
                    ExperimentStates[i]="Recording";
                }
                GenerationW=light?panel.Power:0;
                double baseLoad=Vehicle[1].Power;
                double operationsLoad=scienceLoad+(send?antenna.Power:0);
                RequestedW=baseLoad+operationsLoad;
                // Share a scarce energy reserve across commanded operations. This
                // represents duty cycling rather than creating energy or losing data.
                double availableW=GenerationW+Charge/dt;
                double duty=operationsLoad>0?Math.Min(1,Math.Max(0,availableW-baseLoad)/operationsLoad):1;
                bool powered=bus&&availableW+1e-8>=baseLoad;
                if(!powered)duty=0;
                DemandW=Math.Min(availableW,baseLoad+operationsLoad*duty);
                Charge=Math.Max(0,Math.Min(panel.A,Charge+(GenerationW-DemandW)*dt));
                MinimumCharge=Math.Min(MinimumCharge,Charge);
                bool limited=bus && (duty<.999 || !powered);
                if(limited)UnpoweredSeconds+=step*(1-duty);
                if(limited!=powerLimited){Log(limited?"Power shortage: experiments and downlink are duty cycling.":"Power supply recovered; full operations available.");powerLimited=limited;}
                if(requestScience&&requested>0&&full)RecorderFullSeconds+=step;
                if(full!=recorderLimited && requested>0){if(full)Log("Recorder full. Downlink data to make room for new science.");recorderLimited=full;}
                double scienceData=Math.Min(Math.Max(0,Vehicle[1].A-Stored),collectionRate*dt*duty);
                ScienceRate=dt>0?scienceData/dt:0;
                if(scienceData>0){
                    ScienceSeconds+=step*duty;
                    for(int i=0;i<5;i++)if(active[i]){
                        double gather=scienceData*Vehicle.Catalog[5][i].A/collectionRate;
                        StoredByInstrument[i]+=gather;CollectedByInstrument[i]+=gather;
                        if(eligibleOrbit&&i==Mission.Instrument){qualifiedStored+=gather;QualifiedCollected+=gather;}
                        if(duty<.999)ExperimentStates[i]="Recording at reduced duty";
                    }
                    Stored+=scienceData;Collected+=scienceData;
                }else if(collectionRate>0&&duty<=0){for(int i=0;i<5;i++)if(active[i])ExperimentStates[i]="Waiting for battery charge";}
                ScienceState=!requestScience?"Standby":requested==0?"No experiments armed":!bus?"No avionics or power system":full?"Recorder full":scienceData>0?(eligibleOrbit?"Recording on assigned orbit":"Recording outside assigned orbit"):collectionRate>0?"Waiting for power":"Waiting for observation window";
                DownlinkRate=0;
                if(send){
                    double data=Math.Min(Stored,MissionEnvironment.LinkRate(Vehicle,Reference,distance)*dt*duty);
                    double qualifying=Stored>0?data*qualifiedStored/Stored:0;
                    for(int i=0;i<5;i++){
                        double share=Stored>0?data*StoredByInstrument[i]/Stored:0;
                        StoredByInstrument[i]=Math.Max(0,StoredByInstrument[i]-share);ReturnedByInstrument[i]+=share;
                    }
                    qualifiedStored=Math.Max(0,qualifiedStored-qualifying);
                    Stored=Math.Max(0,Stored-data);Returned+=data;if(now-ScienceStart<=Mission.Hours*3600)QualifiedReturned+=qualifying;
                    DownlinkRate=data/dt;
                    if(data>0)DownlinkSeconds+=step*duty;
                }
                if((mode&2)!=0&&Stored>1e-8&&!contact)LinkBlockedSeconds+=step;
                RadioState=(mode&2)==0?"Standby":!Vehicle.Has(4)?"No antenna installed":!Vehicle.Has(6)?"No ground network":!bus?"No avionics or power system":Stored<=1e-8?"Recorder empty":!contact?"Earth is out of view":duty<=0?"Waiting for battery charge":string.Format("Sending {0:0.000} GB/h",DownlinkRate);
                Sample();
            }
            UpdateOrbit();
            if(Finished)return;
            if(OnPlan&&EngineFiring)Inserted=true;
            if(!Success && !DeadlineMissed && QualifiedReturned>=Mission.Dose && (!enforceBudget||Vehicle.Cost<=Mission.Budget)){
                Success=true;Status="Mission complete. Required science received on Earth.";
                Log(Status);History.Add(new FlightSample(this));
            }
            else if(!Success && !DeadlineMissed && Time-ScienceStart>=Mission.Hours*3600){
                DeadlineMissed=true;Status="Mission deadline passed. Flight continues.";
                Log(Status);History.Add(new FlightSample(this));
            }
        }
    }
}

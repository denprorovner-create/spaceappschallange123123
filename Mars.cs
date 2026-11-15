using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Perilune {
    sealed class MarsPart {
        public readonly string Name,Detail;
        public readonly double Mass,Cost,Value;
        public MarsPart(string name,string detail,double mass,double cost,double value){
            Name=name;Detail=detail;Mass=mass;Cost=cost;Value=value;
        }
    }
    sealed class RoverBuild {
        public static readonly string[] Families={"Chassis","Heat shield","Parachute","Descent engine","Power","Radio","Science"};
        public static readonly MarsPart[][] Parts={
            new[]{new MarsPart("Scout","Agile six-wheel rover · 0.9 GB",190,18,.9),new MarsPart("Surveyor","Stable six-wheel rover · 2.4 GB",360,29,2.4)},
            new[]{
                new MarsPart("No heat shield","Entry vehicle has no thermal protection",0,0,0),
                new MarsPart("Ablative aeroshell","28 MJ/m² thermal capacity · 1.15 MW/m² peak",88,8,1),
                new MarsPart("Reinforced aeroshell","46 MJ/m² thermal capacity · 1.90 MW/m² peak",134,15,1.65)
            },
            new[]{new MarsPart("No parachute","Descent must rely on the engine",0,0,0),new MarsPart("Disk-gap-band","16 m canopy · light payloads",32,7,200),new MarsPart("Heavy canopy","22 m canopy · more drag",57,12,380)},
            new[]{new MarsPart("No descent engine","Parachute only",0,0,0),new MarsPart("Throttleable lander","8 kN · 118 kg propellant",145,16,8000),new MarsPart("Heavy descent stage","13 kN · 165 kg propellant",203,24,13000)},
            new[]{new MarsPart("Solar deck","180 W in sunlight · 240 Wh",28,9,240),new MarsPart("Radioisotope unit","110 W continuous · 420 Wh",51,18,420)},
            new[]{new MarsPart("UHF relay","0.65 GB/h through a relay orbiter",17,7,.65),new MarsPart("High-rate relay","2.1 GB/h · higher power draw",33,14,2.1)},
            new[]{new MarsPart("Panoramic camera","Survey exposed strata and outcrops",9,5,.12),new MarsPart("Mineral spectrometer","Identify mineral signatures at rocks",18,8,.08),new MarsPart("Core sampler","Drill and log a sample for caching",49,14,.05),
                new MarsPart("Weather station","Record pressure, temperature and density",6,4,.025)}
        };
        public readonly int[] Fitted={0,1,1,1,0,0};
        public readonly bool[] Instruments={true,true,false,false};
        public const double Budget=84;
        public MarsPart Item(int i){return Parts[i][Fitted[i]];}
        public double Cost { get {return Enumerable.Range(0,6).Sum(i=>Item(i).Cost)+Science.Sum(i=>Parts[6][i].Cost);}}
        public double Mass { get {return Enumerable.Range(0,6).Sum(i=>Item(i).Mass)+Science.Sum(i=>Parts[6][i].Mass);}}
        public double LandingFuel {get{return Fitted[3]==1?118:Fitted[3]==2?165:0;}}
        public IEnumerable<int> Science {get {return Enumerable.Range(0,Instruments.Length).Where(i=>Instruments[i]);}}
        public RoverBuild Copy(){
            var result=new RoverBuild();
            Array.Copy(Fitted,result.Fitted,Fitted.Length);Array.Copy(Instruments,result.Instruments,Instruments.Length);
            return result;
        }
    }
    sealed class MarsSite {
        public readonly string Name,Geology;
        public readonly Point Point;
        public MarsSite(string name,string geology,double x,double z){Name=name;Geology=geology;Point=new Point(x,z);}
    }
    sealed class MarsFlight {
        public static readonly Point[] FieldBoulders={
            new Point(-9,7),new Point(21,8),new Point(-28,18),new Point(7,37),
            new Point(18,47),new Point(-39,52),new Point(-9,63),new Point(7,74),
            new Point(39,75),new Point(-16,87),new Point(21,95)
        };
        public readonly List<Point> Traverse=new List<Point>();
        public static readonly MarsSite[] Sites={
            new MarsSite("Basalt block","Coarse igneous texture · iron-rich basalt candidate",14,20),
            new MarsSite("Layered outcrop","Fine layers · candidate sedimentary deposit",-19,39),
            new MarsSite("Delta ridge","Rounded clasts · possible transported material",29,60),
            new MarsSite("Light-toned vein","Bright mineral fill · carbonate candidate",-31,69)
        };
        const double FuelExhaustVelocity=280*9.80665;
        const double SuttonGravesMars=1.9027e-4, NoseRadiusMeters=.88;
        const double MaxShieldHeatFlux=1.15e6, MaxShieldHeatLoad=28e6;
        public const double ChuteMachLimit=2.05,ChutePressureLimit=1050,ChuteMinPressure=22;
        public readonly RoverBuild Build;
        public readonly MarsAtmosphere Atmosphere;
        public readonly bool[,] Survey=new bool[Sites.Length,RoverBuild.Parts[6].Length];
        public readonly List<string> Log=new List<string>();
        public double Timer,Height,Fall,Forward,Fuel,Pressure,PeakPressure;
        public double StaticPressure,Density,Temperature,Mach,HeatFlux,PeakHeatFlux,HeatLoad,EntryAccel,PeakEntryAccel,Downrange;
        public double BankAngle=65,ChuteInflation,LandingSteer,LateralMps,Crossrange,LandingHeightOffset;
        public const double LandingSteerLimitM=85;
        public double ChuteAt=-1,ShieldAt=-1,PoweredDescentAt=-1;
        readonly double entryHeight;
        double exposedHeatLoad;
        public double Throttle,SurfaceClock,Heading,Speed,Distance,Charge,Stored,Returned;
        public double RadioDelaySeconds,InTransit;
        readonly Queue<SurfaceReturn> surfaceReturns=new Queue<SurfaceReturn>();
        SurfaceReturn queuedReturn;
        public Point Position;
        public bool Assist=true,Chute,ChuteSpent,Shield,Engine,Landed,Failed,Radio,InOperation;
        public int SelectedScience,ActiveSite=-1,ActiveInstrument=-1,Samples;
        public double Progress;
        public Func<Point,double> TerrainHeight;
        public const double ReturnGoal=.20;
        public int SurveyedSites {get{return Enumerable.Range(0,Sites.Length).Count(site=>Build.Science.Any(i=>Survey[site,i]));}}
        public bool ObjectiveComplete {get{return SurveyedSites>=2&&Returned>=ReturnGoal;}}
        public string Status="Approach from Mars arrival. Watch speed and altitude.";
        public double Clearance {get{return Height-LandingHeightOffset;}}
        public double HorizontalSpeed {get{return Math.Sqrt(Forward*Forward+LateralMps*LateralMps);}}
        public double SpeedAir {get{return Math.Sqrt(Fall*Fall+HorizontalSpeed*HorizontalSpeed);}}
        public double FlightPathAngleDegrees {get{return Math.Atan2(-Fall,HorizontalSpeed)*180/Math.PI;}}
        double CrossrangeAcceleration(){
            double target=Limit(LandingSteer,-1,1)*14;
            // Begin braking before the surveyed patch boundary. Leave a margin
            // for the steering controller's finite response time.
            double right=Math.Sqrt(2*2*Math.Max(0,LandingSteerLimitM-8-Crossrange));
            double left=Math.Sqrt(2*2*Math.Max(0,LandingSteerLimitM-8+Crossrange));
            target=Limit(target,-left,right);
            return Limit((target-LateralMps)*.9,-3.4,3.4);
        }
        public double ChuteSpeedLimitAtAltitude {get{return ChuteMachLimit*Math.Sqrt(MarsAtmosphere.Gamma*MarsAtmosphere.GasConstant*Temperature);}}
        public const double ChuteSpeedLimit=520; // Approximate limit near the field. Use Mach for the live envelope.
        public bool CanDeployChute {get{return !Failed&&!Landed&&!Chute&&!ChuteSpent&&!Engine&&Build.Fitted[2]>0&&
            Mach<=ChuteMachLimit&&Pressure>=ChuteMinPressure&&Pressure<=ChutePressureLimit&&Clearance>75;}}
        public bool CanJettisonShield {get{return Shield&&!Failed&&!Landed&&Mach<.9&&HeatFlux<3200;}}
        public string DeploymentMessage {
            get {
                if(Engine)return Fuel>0?"Powered descent · land below 3.7 m/s down.":"Descent propellant depleted.";
                if(Chute)return Shield?(CanJettisonShield?"Heat shield clear to release.":"Wait for the chute to slow the capsule."):"Canopy open · prepare for powered descent.";
                if(ChuteSpent||Build.Fitted[2]==0)return "Parachute unavailable.";
                if(CanDeployChute)return "Parachute deployment window open.";
                if(Mach>ChuteMachLimit||Pressure>ChutePressureLimit)return "Outside parachute envelope · wait for braking.";
                return Pressure<ChuteMinPressure?"Too little airflow to inflate the parachute.":"Too little altitude for canopy inflation.";
            }
        }
        public double Mass {get{return Build.Mass-(InitialFuel-Fuel)-(Shield?0:Build.Item(1).Mass)-(ChuteSpent?Build.Item(2).Mass:0);}}
        public double InitialFuel {get{return Build.LandingFuel;}}
        public double Capacity {get{return Build.Item(0).Value;}}
        public double NextDeliverySeconds {get{return surfaceReturns.Count==0?0:Math.Max(0,surfaceReturns.Peek().DeliveryTime-SurfaceClock);}}
        double RelayRadiusKm {get{return Atmosphere.Radius(0)/1000+420;}}
        double RelayPeriod {get{return Orbit.Circle*Math.Sqrt(Math.Pow(RelayRadiusKm,3)/42828.375816);}}
        double RelayHalfWindow {get{return Math.Acos((RelayRadiusKm-420)/RelayRadiusKm)*RelayPeriod/Orbit.Circle;}}
        static double Cycle(double value,double period){return (value%period+period)%period;}
        public bool Contact {
            get {
                if(RadioDelaySeconds<=0)return Cycle(SurfaceClock+65,275)<110;
                double period=RelayPeriod,phase=Cycle(SurfaceClock-165,period),edge=RelayHalfWindow;
                return phase<edge||phase>period-edge;
            }
        }
        public double NextContactSeconds {
            get {
                if(Contact)return 0;
                if(RadioDelaySeconds<=0)return 275-Cycle(SurfaceClock+65,275);
                double period=RelayPeriod;
                return period-RelayHalfWindow-Cycle(SurfaceClock-165,period);
            }
        }
        public bool Sunlit {get{return RadioDelaySeconds>0?(SurfaceClock+21000)%88642.6848<48000:SurfaceClock%640<465;}}
        public int ClosestSite {
            get {
                int best=0;
                for(int i=1;i<Sites.Length;i++)if((Sites[i].Point-Position).LengthSquared<(Sites[best].Point-Position).LengthSquared)best=i;
                return best;
            }
        }
        public MarsFlight(RoverBuild design):this(design,12600,77,421){}
        public MarsFlight(RoverBuild design,double heightM,double downMps,double horizontalMps,MarsAtmosphere atmosphere=null){
            Build=design.Copy();
            Atmosphere=atmosphere??new MarsAtmosphere();
            if(!Finite(heightM)||heightM<0)throw new ArgumentOutOfRangeException("heightM");
            if(!Finite(downMps))throw new ArgumentOutOfRangeException("downMps");
            if(!Finite(horizontalMps)||horizontalMps<0)throw new ArgumentOutOfRangeException("horizontalMps");
            Height=entryHeight=heightM;Fall=downMps;Forward=horizontalMps;
            Shield=Build.Fitted[1]!=0;Fuel=InitialFuel;Charge=Build.Item(4).Value;
            SelectedScience=Build.Science.DefaultIfEmpty(0).First();
            RefreshAir();
            if(heightM>=100000)Note("Entry interface · aeroshell forward. Let drag shed arrival speed.");
        }
        static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        static double Limit(double value,double low,double high){return Math.Max(low,Math.Min(high,value));}
        void Note(string value){Status=value;Log.Add(value);}
        public void DeployChute(){
            if(Chute||ChuteSpent||Failed||Landed||Engine)return;
            if(Build.Fitted[2]==0){Note("No parachute installed.");return;}
            if(Mach>ChuteMachLimit||Pressure>ChutePressureLimit){ChuteSpent=true;Note("Parachute tore: outside Mach or dynamic-pressure limits.");return;}
            if(Pressure<ChuteMinPressure){Note("Too little airflow to inflate the parachute.");return;}
            if(Clearance<=75){Note("Insufficient altitude for canopy inflation.");return;}
            Chute=true;ChuteAt=Timer;ChuteInflation=0;
            Note(string.Format("Parachute deployed · Mach {0:0.00}, {1:0} Pa dynamic pressure.",Mach,Pressure));
        }
        public void JettisonShield(){
            if(!Shield||Failed||Landed)return;
            bool safe=CanJettisonShield;
            Shield=false;ShieldAt=Timer;
            Note(safe?"Heat shield separated.":"Heat shield separated outside its thermal window.");
        }
        public void StartDescent(){
            if(Engine||Failed||Landed)return;
            if(Build.Fitted[3]==0){Note("No descent engine installed.");return;}
            if(Fuel<=0){Note("No descent propellant remaining.");return;}
            if(Shield)JettisonShield();
            Engine=true;PoweredDescentAt=Timer;Chute=false;ChuteSpent=true;Throttle=Math.Max(.35,Throttle);
            Note("Backshell away. Powered descent.");
        }
        public void TickEntry(double seconds){
            if(!Finite(seconds)||seconds<=0)return;
            while(seconds>1e-6&&!Failed&&!Landed){
                double dt=Math.Min(.04,seconds);
                StepEntry(dt);seconds-=dt;
            }
        }
        void RefreshAir(){
            MarsAir air=Atmosphere.Sample(Height);
            StaticPressure=air.StaticPressurePa;Density=air.DensityKgM3;Temperature=air.TemperatureK;
            double speed=SpeedAir;
            Pressure=.5*Density*speed*speed;
            Mach=speed/air.SoundSpeedMps;
            HeatFlux=SuttonGravesMars*Math.Sqrt(Density/NoseRadiusMeters)*speed*speed*speed;
            PeakPressure=Math.Max(Pressure,PeakPressure);
            PeakHeatFlux=Math.Max(HeatFlux,PeakHeatFlux);
        }
        double EntryDragArea(){
            if(!Shield)return 2.1;
            // Blunt aeroshell drag transitions through subsonic and transonic flow.
            double coefficient=Mach<1?1.05+.42*Mach:1.47+.07*Limit(Mach-1,0,1);
            return 4.1*coefficient;
        }
        void GuideDescent(double gravity){
            if(!Engine&&CanDeployChute&&Height<25000&&Fall>0)DeployChute();
            if(Chute&&CanJettisonShield&&Timer-ChuteAt>=5)JettisonShield();
            if(!Engine&&Fall>0&&Fuel>0){
                double available=Build.Item(3).Value/Math.Max(1,Mass);
                double stoppingAcceleration=Math.Max(.5,available*.76-gravity);
                double stoppingDistance=Fall*Fall/(2*stoppingAcceleration);
                double horizontalLimited=Math.Min(180,Forward*.5);
                double startHeight=Math.Max(320,stoppingDistance*1.32+horizontalLimited+110);
                if(Clearance<startHeight&&Clearance<5500&&Mach<.9)StartDescent();
            }
            if(!Engine)return;
            double target=Math.Min(62,Math.Max(1.45,Math.Sqrt(4.5*Math.Max(0,Clearance-4))));
            double vertical=Math.Max(0,gravity+(Fall-target)*.97);
            double horizontal=Math.Min(8,Forward*.62), lateral=CrossrangeAcceleration();
            Throttle=Limit(Math.Sqrt(vertical*vertical+horizontal*horizontal+lateral*lateral)*Math.Max(1,Mass)/Build.Item(3).Value,0,1);
        }
        void StepEntry(double dt){
            RefreshAir();
            HeatLoad+=HeatFlux*dt;
            if(!Shield)exposedHeatLoad+=Math.Max(0,HeatFlux-800)*dt;
            double shieldCapacity=Build.Item(1).Value;
            if(Shield&&(HeatFlux>MaxShieldHeatFlux*shieldCapacity||HeatLoad>MaxShieldHeatLoad*shieldCapacity)){
                Failed=true;Note("Thermal protection exceeded its design limit.");return;
            }
            if(!Shield&&(HeatFlux>16000||exposedHeatLoad>120000||Pressure>4500)){
                Failed=true;Note("The exposed rover failed during atmospheric entry.");return;
            }
            if(Chute&&(Pressure>ChutePressureLimit*1.12||Mach>ChuteMachLimit*1.08)){
                Chute=false;ChuteSpent=true;Note("Parachute tore under load.");
            }
            if(Chute&&ChuteInflation<1){
                ChuteInflation=Limit((Timer-ChuteAt)/1.45,0,1);
                if(ChuteInflation>=1)Note("Parachute inflated.");
            }
            double gravity=Atmosphere.Gravity(Height);
            if(Assist)GuideDescent(gravity);
            double canopyArea=Chute?.68*Build.Item(2).Value*ChuteInflation*ChuteInflation:0;
            double dragArea=EntryDragArea()+canopyArea;
            double mass=Math.Max(1,Mass);
            double airspeed=SpeedAir;
            double drag=Pressure*dragArea/mass;
            double braking=airspeed>1e-8?drag/airspeed:0;
            double lift=Shield&&!Chute&&!Engine&&Mach>1.5
                ?drag*.24*Math.Cos(BankAngle*Math.PI/180):0;
            EntryAccel=Math.Sqrt(drag*drag+lift*lift);
            PeakEntryAccel=Math.Max(EntryAccel,PeakEntryAccel);
            double vertical=0,horizontal=0,lateral=0;
            if(Engine&&Throttle>0&&Fuel>0){
                double force=Build.Item(3).Value*Limit(Throttle,0,1);
                double use=Math.Min(Fuel,force*dt/FuelExhaustVelocity);
                Fuel-=use;
                double available=use*FuelExhaustVelocity/dt/mass;
                horizontal=Math.Min(8,Forward*.62);
                lateral=CrossrangeAcceleration();
                double translation=Math.Sqrt(horizontal*horizontal+lateral*lateral);
                double tiltLimit=available*.62;
                if(translation>tiltLimit){
                    horizontal*=tiltLimit/translation;
                    lateral*=tiltLimit/translation;
                }
                vertical=Math.Sqrt(Math.Max(0,available*available-horizontal*horizontal-lateral*lateral));
            }
            double radius=Atmosphere.Radius(Height),previousFall=Fall,previousForward=Forward,previousLateral=LateralMps;
            double liftPerSpeed=airspeed>1e-8?lift/airspeed:0;
            Fall+=(gravity-HorizontalSpeed*HorizontalSpeed/radius-braking*Fall-liftPerSpeed*Forward-vertical)*dt;
            Forward=Math.Max(0,Forward+(previousFall*Forward/radius-braking*Forward-liftPerSpeed*previousFall-horizontal)*dt);
            LateralMps+=(previousFall*LateralMps/radius-braking*LateralMps+lateral)*dt;
            Height-=(previousFall+Fall)*.5*dt;
            Downrange+=(previousForward+Forward)*.5*dt*Atmosphere.Radius(0)/radius;
            Crossrange+=(previousLateral+LateralMps)*.5*dt*Atmosphere.Radius(0)/radius;
            Timer+=dt;
            RefreshAir();
            if(Fall<0&&Height>Math.Max(130000,entryHeight+2500)&&Forward>Math.Sqrt(gravity*radius)){
                Failed=true;Note("Atmospheric skip-out. Target a steeper entry corridor.");return;
            }
            if(Clearance>0)return;
            Height=LandingHeightOffset;
            if(Math.Abs(Fall)>3.7||HorizontalSpeed>3.0){Failed=true;Note(string.Format("Hard impact · {0:0.0} m/s vertical, {1:0.0} m/s horizontal.",Fall,HorizontalSpeed));}
            else {
                Landed=true;Engine=false;Chute=false;Position=new Point(Crossrange,0);
                Traverse.Add(Position);
                Note("Touchdown confirmed. Drive to a science site.");
            }
        }
        public void Drive(double throttle,double steer,double dt){
            if(!Landed)return;
            if(Charge<.01||InOperation)throttle=0;
            double limit=Build.Fitted[0]==0?3.2:2.15;
            Heading+=steer*(.45+.36*Math.Abs(Speed))*dt;
            double desired=throttle*limit;
            if(TerrainHeight!=null&&Math.Abs(throttle)>.001){
                var ahead=Position+new Vector(Math.Sin(Heading),Math.Cos(Heading))*Math.Sign(throttle)*1.5;
                double rise=(TerrainHeight(ahead)-TerrainHeight(Position))/1.5;
                desired*=Math.Max(.20,Math.Min(1,1-Math.Max(0,rise)*3.4));
            }
            double change=Math.Min(4.2*dt,Math.Abs(desired-Speed));
            Speed+=Math.Sign(desired-Speed)*change;
            var step=new Vector(Math.Sin(Heading)*Speed*dt,Math.Cos(Heading)*Speed*dt);
            var at=Position+step;
            bool blocked=step.LengthSquared>0&&FieldBoulders.Any(rock=>(at-rock).LengthSquared<2.8&&(at-rock).LengthSquared<(Position-rock).LengthSquared);
            if(blocked){
                Speed=0;
                if(Status!="Boulder ahead. Reverse or steer around it.")Note("Boulder ahead. Reverse or steer around it.");
            }else if(Math.Abs(at.X)<102&&at.Y> -38&&at.Y<124){
                Position=at;Distance+=step.Length;
                if(Traverse.Count==0||(Traverse[Traverse.Count-1]-Position).Length>=1.5)Traverse.Add(Position);
            }else Speed=0;
        }
        public void StartExperiment(){
            int site=ClosestSite, instrument=SelectedScience;
            if(!Landed||InOperation)return;
            if(!Build.Instruments[instrument]){Note("Fit a science instrument in the workshop.");return;}
            if((Sites[site].Point-Position).Length>(instrument==0||instrument==3?12:4.5)){Note("Drive closer to "+Sites[site].Name+".");return;}
            if(Math.Abs(Speed)>.1){Note("Stop the rover to start the observation.");return;}
            if(Survey[site,instrument]){Note("Already recorded here. Try another site or instrument.");return;}
            if(Stored+RoverBuild.Parts[6][instrument].Value>Capacity){Note("Recorder full. Downlink your results first.");return;}
            Progress=0;ActiveSite=site;ActiveInstrument=instrument;InOperation=true;
            Note("Acquiring "+RoverBuild.Parts[6][instrument].Name.ToLowerInvariant()+" data.");
        }
        public void TickSurface(double dt){
            if(!Landed||!Finite(dt)||dt<=0)return;
            while(dt>1e-7){
                double step=Math.Min(4,dt);
                StepSurface(step);
                dt-=step;
            }
        }
        void StepSurface(double dt){
            SurfaceClock+=dt;
            while(surfaceReturns.Count>0&&surfaceReturns.Peek().DeliveryTime<=SurfaceClock){
                var delivery=surfaceReturns.Dequeue();
                InTransit=Math.Max(0,InTransit-delivery.Data);
                Returned+=delivery.Data;
            }
            bool sending=Radio&&Contact&&Stored>1e-8;
            double generation=Build.Fitted[4]==1?110:Sunlit?180:0;
            double watts=43+Math.Abs(Speed)*44+(InOperation?new[]{54,86,132,23}[ActiveInstrument]:0)+(sending?(Build.Fitted[5]==0?55:124):0);
            double available=generation+Charge*3600/Math.Max(.001,dt);
            double duty=Math.Max(0,Math.Min(1,(available-43)/Math.Max(1,watts-43)));
            Charge=Math.Max(0,Math.Min(Build.Item(4).Value,Charge+(generation-Math.Min(available,watts))*dt/3600));
            if(sending){
                double amount=Math.Min(Stored,Build.Item(5).Value*dt/3600*duty);
                Stored-=amount;
                if(amount>0)Transmit(amount);
            }
            if(!InOperation)return;
            Progress+=dt*duty/new[]{9d,17d,28d,10d}[ActiveInstrument];
            if(Progress<1)return;
            Survey[ActiveSite,ActiveInstrument]=true;InOperation=false;
            if(ActiveInstrument==2)Samples++;
            Stored+=RoverBuild.Parts[6][ActiveInstrument].Value;
            if(ActiveInstrument==3){
                double relativeHeight=TerrainHeight==null?0:TerrainHeight(Position)-Atmosphere.DatumElevationM;
                var sample=Atmosphere.Sample(relativeHeight);
                Note(string.Format("{0} weather (modeled) · {1:F0} Pa · {2:F1} K · {3:F4} kg/m³.",
                    Sites[ActiveSite].Name,sample.StaticPressurePa,sample.TemperatureK,sample.DensityKgM3));
            }else Note(Sites[ActiveSite].Name+" · "+Sites[ActiveSite].Geology);
        }
        void Transmit(double amount){
            if(RadioDelaySeconds<=0){Returned+=amount;return;}
            // Group the continuous stream in subsecond packets. Crossing the
            // relay horizon after transmission does not erase packets in flight.
            double deliveryTime=Math.Ceiling((SurfaceClock+RadioDelaySeconds)*2)*.5;
            if(queuedReturn==null||queuedReturn.DeliveryTime!=deliveryTime){
                queuedReturn=new SurfaceReturn(deliveryTime);
                surfaceReturns.Enqueue(queuedReturn);
            }
            queuedReturn.Data+=amount;
            InTransit+=amount;
        }
        sealed class SurfaceReturn {
            public readonly double DeliveryTime;
            public double Data;
            public SurfaceReturn(double deliveryTime){DeliveryTime=deliveryTime;}
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace Perilune {
    // Geometry uses the same ecliptic-frame Horizons vectors as the flight integrator.
    // Spherical occultation, ideal Sun-pointing arrays and fictional ground sites are
    // deliberate educational approximations, not a full operational link budget.
    static class MissionEnvironment {
        public static Vector3D SunVector(SolarSystem system,Body body) {
            return system.Positions[0]-system.Positions[body.Lunar?4:3];
        }
        public static Vector3D EarthVector(SolarSystem system) {
            return system.Positions[3]-system.Positions[4];
        }
        public static bool Sunlit(Body body,Vector3D position,Vector3D sun) {
            var direction=sun;direction.Normalize();
            double along=Vector3D.DotProduct(position,direction);
            return along>=0 || (position-direction*along).Length>body.Radius;
        }
        public static bool ScienceWindow(Mission mission,Vector3D position,int instrument,Vector3D sun,Vector3D earth) {
            if(position.Length<=mission.Body.Radius+(mission.Body.Lunar?0:100))return false;
            double latitude=Math.Abs(position.Y)/position.Length;
            double altitude=position.Length-mission.Body.Radius;
            var direction=sun;direction.Normalize();
            double daylight=Vector3D.DotProduct(position,direction)/position.Length;
            // Reflected-light and lunar mineral spectroscopy need a lit surface;
            // thermal, radar, relay and magnetic measurements can operate in darkness.
            if(instrument==0 && daylight<=0)return false;
            if(instrument==1 && mission.Body.Lunar && daylight<=0)return false;
            if(instrument!=mission.Instrument)return true;
            switch(mission.Name){
                case "Polar Watch": case "Auroral Arc": case "Dark Ice": return latitude>.866;
                case "Long Baseline": case "Lunar Pathfinder": return altitude>(mission.Ap+mission.Pe)/2;
                case "Far Side Relay": return Vector3D.DotProduct(position,earth)<-.2*position.Length*earth.Length;
                case "Magnetic Fossils": return altitude<180;
                case "Terminator Chase": return daylight>0 && daylight<.37;
                case "Crater Echo": return altitude<500;
                default:return true;
            }
        }
        public static bool Contact(Vehicle vehicle,Body body,Vector3D position,double seconds,Vector3D earth,out double distance) {
            distance=double.PositiveInfinity;
            if(!vehicle.Has(4)||!vehicle.Has(6))return false;
            var earthRelative=body.Lunar?position-earth:position;
            if(body.Lunar){
                var ray=earth-position;
                double fraction=Math.Max(0,Math.Min(1,-Vector3D.DotProduct(position,ray)/ray.LengthSquared));
                if((position+ray*fraction).Length<body.Radius)return false;
            }
            int count=(int)vehicle[6].A;
            for(int i=0;i<count;i++){
                double angle=Orbit.Circle*(seconds/Body.Earth.Day+i/(double)count);
                double latitude=(i%3-1)*.55;
                var station=new Vector3D(Math.Cos(angle)*Math.Cos(latitude),Math.Sin(latitude),Math.Sin(angle)*Math.Cos(latitude));
                if(Vector3D.DotProduct(earthRelative,station)>Body.Earth.Radius)
                    distance=Math.Min(distance,(earthRelative-station*Body.Earth.Radius).Length);
            }
            return !double.IsPositiveInfinity(distance);
        }
        public static double LinkRate(Vehicle vehicle,Body body,double distance) {
            if(double.IsInfinity(distance)||distance<=0||!vehicle.Has(4))return 0;
            double reference=body.Lunar?400000:2000;
            return vehicle[4].A*Math.Min(1,reference*reference/(distance*distance));
        }
    }

    sealed class DesignAnalysis {
        public double MassKg,Cost,BudgetMargin,PadTwr,AscentDeltaV,OrbitalDeltaV;
        public double GenerationW,IdleW,ScienceW,TransmitW,PeakW,PowerMarginW;
        public double EclipseMinutes,BatteryMinutes,RecorderHours,ReturnHours;
        public double ContactFraction,ScienceFraction,EstimatedReturnGb,MeanPowerW,MeanLoadW;
        public readonly List<string> Advice=new List<string>();
    }
    sealed class MissionReport {
        public int Score,ScienceScore,OrbitScore,ResourceScore;
        public string Title;
        public readonly List<string> Findings=new List<string>();
    }
    struct FlightSample {
        public readonly double Time,Charge,Stored,Returned,QualifiedReturned,GenerationW,DemandW;
        public FlightSample(Flight flight) {
            Time=flight.Time;Charge=flight.Charge;Stored=flight.Stored;Returned=flight.Returned;
            QualifiedReturned=flight.QualifiedReturned;GenerationW=flight.GenerationW;DemandW=flight.DemandW;
        }
    }
    sealed class FlightEvent {
        public readonly double Time;
        public readonly string Message;
        public FlightEvent(double time,string message){Time=time;Message=message;}
        public override string ToString(){return TimeSpan.FromSeconds(Math.Max(0,Time)).ToString(@"d\.hh\:mm\:ss")+"  "+Message;}
    }
    static class MissionAnalysis {
        public static DesignAnalysis Analyze(Mission mission,Vehicle vehicle) {
            var a=new DesignAnalysis {
                MassKg=vehicle.Stack.LaunchMass(vehicle.Wet),Cost=vehicle.Cost,
                BudgetMargin=mission.Budget-vehicle.Cost,PadTwr=vehicle.Stack.PadTwr(vehicle.Wet,Body.Earth),
                AscentDeltaV=vehicle.Stack.IdealDv(vehicle.Wet),OrbitalDeltaV=vehicle.FuelDV(vehicle[2].A)*1000,
                GenerationW=vehicle[3].Power,IdleW=vehicle[1].Power,ScienceW=vehicle.InstrumentPower,
                TransmitW=vehicle[4].Power
            };
            a.PeakW=a.IdleW+a.ScienceW+a.TransmitW;
            a.PowerMarginW=a.GenerationW-a.PeakW;
            a.BatteryMinutes=a.PeakW>0?vehicle[3].A/a.PeakW*60:0;
            a.RecorderHours=vehicle.InstrumentRate>0?vehicle[1].A/vehicle.InstrumentRate:double.PositiveInfinity;
            var system=new SolarSystem();
            var sun=MissionEnvironment.SunVector(system,mission.Body);var earth=MissionEnvironment.EarthVector(system);
            var orbit=mission.Target;
            // Sample a full day (or a full orbit if longer) at uniform time intervals.
            double duration=Math.Max(86400,orbit.Period),interval=duration/1440;
            double longestEclipse=0,eclipse=0,linkRate=0,scienceRate=0,otherScienceRate=0;
            int[] instruments=vehicle.InstalledInstruments.ToArray();
            for(int i=0;i<1440;i++){
                double time=(i+.5)*interval;var p=orbit.Position(time);
                bool light=MissionEnvironment.Sunlit(mission.Body,p,sun);
                if(light){a.MeanPowerW+=a.GenerationW/1440;eclipse=0;}
                else {eclipse+=interval;longestEclipse=Math.Max(longestEclipse,eclipse);}
                double distance;
                bool contact=MissionEnvironment.Contact(vehicle,mission.Body,p,time,earth,out distance);
                if(contact){a.ContactFraction+=1d/1440;linkRate+=MissionEnvironment.LinkRate(vehicle,mission.Body,distance)/1440;}
                bool available=MissionEnvironment.ScienceWindow(mission,p,mission.Instrument,sun,earth);
                if(available){a.ScienceFraction+=1d/1440;if(vehicle.HasInstrument(mission.Instrument))scienceRate+=Vehicle.Catalog[5][mission.Instrument].A/1440;}
                double instrumentLoad=0;
                foreach(int instrument in instruments){
                    if(!MissionEnvironment.ScienceWindow(mission,p,instrument,sun,earth))continue;
                    var part=Vehicle.Catalog[5][instrument];
                    instrumentLoad+=part.Power;
                    if(instrument!=mission.Instrument)otherScienceRate+=part.A/1440;
                }
                a.MeanLoadW+=(a.IdleW+instrumentLoad+(contact?a.TransmitW:0))/1440;
            }
            a.EclipseMinutes=longestEclipse/60;
            double powerDuty=a.MeanLoadW<=0?0:Math.Min(1,(a.MeanPowerW+vehicle[3].A/Math.Max(1,mission.Hours))/a.MeanLoadW);
            double share=scienceRate>0?scienceRate/(scienceRate+otherScienceRate):0;
            double returnRate=vehicle.Has(1)&&vehicle.Has(3)&&vehicle.Has(4)&&vehicle.Has(6)?Math.Min(scienceRate,linkRate*share)*powerDuty:0;
            a.ReturnHours=returnRate>0?mission.Dose/returnRate:double.PositiveInfinity;
            a.EstimatedReturnGb=returnRate*mission.Hours;
            if(vehicle.InstalledInstruments.Count()>1)a.Advice.Add(string.Format("{0} experiments add {1:0} kg, draw {2:0} W and can fill the recorder in {3:0.0} h. Switch individual experiments off during tight power or data windows.",vehicle.InstalledInstruments.Count(),vehicle.InstrumentMass,a.ScienceW,a.RecorderHours));
            if(a.PowerMarginW<0)a.Advice.Add(string.Format("Running science and radio together drains the battery at {0:0} W even in sunlight. A larger array adds mass; alternating the tasks uses the hardware you already have.",-a.PowerMarginW));
            else a.Advice.Add(string.Format("{0:0} W remains in full sunlight with every experiment and radio running. During eclipse, the battery supports that load for {1:0} min.",a.PowerMarginW,a.BatteryMinutes));
            if(a.BatteryMinutes<a.EclipseMinutes)a.Advice.Add(string.Format("The estimated longest eclipse is {0:0} min. Turn off science or transmission to stretch the battery through it.",a.EclipseMinutes));
            a.Advice.Add(string.Format("Ground stations can see the planned orbit about {0:0}% of the time. Recorder capacity buffers data until the next pass; an extra station adds contact time without spacecraft mass.",a.ContactFraction*100));
            if(!vehicle.HasInstrument(mission.Instrument))a.Advice.Add("This payload can do other science, but it does not contain the mission's assigned experiment.");
            else if(a.EstimatedReturnGb<mission.Dose)a.Advice.Add("This orbit, payload and link combination is unlikely to return the target data before the deadline. Compare a faster antenna, more stations, or a lighter electrical load.");
            if(a.BudgetMargin<0)a.Advice.Add(string.Format("The design is {0:0} M over budget. You can fly it; the mission's cost objective remains unmet.",-a.BudgetMargin));
            return a;
        }
        static double Limit(double value){return Math.Max(0,Math.Min(1,value));}
        public static MissionReport Report(Flight flight,bool sandbox) {
            var f=flight;var mission=f.Mission;var vehicle=f.Vehicle;
            double elapsed=Math.Max(1,f.Time-f.StartedAt);
            var r=new MissionReport();
            r.ScienceScore=(int)Math.Round(60*Limit(f.QualifiedReturned/Math.Max(.00001,mission.Dose)));
            r.OrbitScore=f.ReachedTarget?20:(f.Current==null||f.Reference!=mission.Body?0:(int)Math.Round(10*Limit(1-(Math.Abs(f.Current.Pe-mission.Pe)+Math.Abs(f.Current.Ap-mission.Ap))/Math.Max(100,mission.Ap+mission.Pe))));
            double budget=sandbox?1:Limit(mission.Budget/Math.Max(.01,vehicle.Cost));
            double power=1-Limit(f.UnpoweredSeconds/elapsed);
            double timely=f.DeadlineMissed?0:1;
            r.ResourceScore=(int)Math.Round(10*budget+5*power+5*timely);
            r.Score=r.ScienceScore+r.OrbitScore+r.ResourceScore;
            r.Title=f.Success?"Mission objective achieved":f.Finished?"Flight ended":f.DeadlineMissed?"Deadline reached · flight continues":"Mission in progress";
            r.Findings.Add(string.Format("Science: {0:0.000} / {1:0.000} GB of qualifying {2} data reached Earth. {3:0.000} GB was collected across all experiments.",f.QualifiedReturned,mission.Dose,Vehicle.Catalog[5][mission.Instrument].Name.ToLowerInvariant(),f.Collected));
            if(!vehicle.HasInstrument(mission.Instrument))r.Findings.Add("The assigned instrument was not installed. Other measurements are recorded, but do not satisfy this mission's science objective.");
            if(f.Collected>f.QualifiedCollected+.001)r.Findings.Add("Some measurements came from other experiments or outside the assigned orbit. Those data remain available, but the mission counts only the requested experiment on its assigned trajectory.");
            r.Findings.Add(f.ReachedTarget?string.Format("Trajectory: the assigned orbit was reached. Time in the orbit tolerance: {0:0.0} min.",f.OnTargetSeconds/60):"Trajectory: the assigned orbit has not been reached. The spacecraft's actual path and target path can be compared in map mode.");
            if(f.RecorderFullSeconds>0)r.Findings.Add(string.Format("The recorder was full for {0:0.0} min of commanded observation. A larger bus, a faster link, or earlier downlinks would preserve more opportunities.",f.RecorderFullSeconds/60));
            if(f.UnpoweredSeconds>1)r.Findings.Add(string.Format("Energy shortage reduced operations for {0:0.0} equivalent min. Separate high-power experiments from downlinks, or increase array and battery capacity.",f.UnpoweredSeconds/60));
            else r.Findings.Add(string.Format("Power: no operations were lost to energy shortage. Lowest battery charge was {0:0} Wh.",f.MinimumCharge));
            if(f.LinkBlockedSeconds>0)r.Findings.Add(string.Format("Earth was out of view for {0:0.0} min while downlink was commanded. A wider ground network can reduce waiting; stored data survive a missed pass.",f.LinkBlockedSeconds/60));
            r.Findings.Add(string.Format("Cost: {0:0} M against a {1:0} M plan. Propellant used after deployment: {2:0.0} kg, delivering {3:0} m/s of commanded Δv.",vehicle.Cost,mission.Budget,f.InitialFuel-f.Fuel,f.DeltaVUsed));
            if(f.DeadlineMissed)r.Findings.Add("The data deadline passed. You can keep flying and collecting, then revise the vehicle or operations plan for another attempt.");
            if(f.Finished)r.Findings.Add("The trajectory intersected the surface or dense atmosphere. Increasing periapsis before the next pass can prevent this loss.");
            return r;
        }
    }
}

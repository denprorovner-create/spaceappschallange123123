using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;

namespace Perilune {
    static class Verification {
        static void Assert(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        static void Fly(Ascent rocket){
            rocket.Start();
            for(int j=0;j<1800 && !rocket.InOrbit && !rocket.Failed;j++)rocket.Tick(1);
        }
        public static bool Run(){
            var log=new System.Text.StringBuilder();
            try{
                var solar=new SolarSystem();
                var original=solar.Positions[3]-solar.Positions[0];
                var momentum=solar.Momentum();
                solar.Advance(86400*30);
                Assert(((solar.Positions[3]-solar.Positions[0])-original).Length>5e7,"Earth did not orbit the Sun");
                Assert(solar.MomentumError(momentum)<.05,"Solar integration violated momentum conservation.");
                Assert(Mission.Archive.Length==15,"Incorrect mission count.");
                foreach(var m in Mission.Archive){
                    var v=new Vehicle();
                    v.Selections[1]=0; v.Selections[5]=m.Instrument;v.Selections[3]=m.Instrument==2?2:1;
                    if(m.Body.Lunar){v.Selections[2]=3;v.Stack=LunarTransfer.StarterStack(m);}
                    var issues=v.Issues(m);
                    Assert(issues.Count==0,m.Name+": "+string.Join("; ",issues));
                    var epoch=new SolarSystem();
                    var departure=LunarTransfer.EarthLaunch(m,epoch);
                    var rocket=new Ascent(departure,v.Stack,v.Wet,epoch);
                    Fly(rocket);
                    var park=Orbit.FromState(Body.Earth,rocket.Position,rocket.Velocity,rocket.Time);
                    Assert(rocket.InOrbit,m.Name+" ascent: "+rocket.Status+" / Pe "+(park==null?-1:park.Pe)+" / Ap "+(park==null?-1:park.Ap));
                    var f=new Flight(m,v,rocket);
                    Assert((f.Position-rocket.Position).Length<1e-8,m.Name+" position discontinuity");
                    if(m.Body.Lunar){
                        Assert(departure.Body==Body.Earth&&f.Reference==Body.Earth,m.Name+" did not depart Earth.");
                        f.DepartForMoon();
                        Assert(f.InLunarTransfer,m.Name+" departure: "+f.Status);
                        f.WarpLunarArrival();
                        Assert(!f.Finished && Math.Abs(f.LunarDistance-m.Pe)<2,m.Name+" lunar approach: "+f.Status+" h="+f.LunarDistance);
                        f.CaptureMoon();
                        Assert(f.Reference==Body.Moon&&f.Current!=null,m.Name+" capture: "+f.Status+" v="+f.Velocity.Length);
                    }
                    f.WarpNode();f.Insert();
                    Assert(f.Inserted&&f.OnPlan,m.Name+" insertion: "+f.Status+" / Pe "+f.Current.Pe+" / Ap "+f.Current.Ap+" plane "+f.Current.Inc+"/"+f.Target.Inc+" node "+f.Current.Node+"/"+f.Target.Node+" arg "+f.Current.Arg+"/"+f.Target.Arg);
                    while(!f.Finished&&!f.Success&&!f.DeadlineMissed)f.Tick(240,3);
                    Assert(f.Success,m.Name+" science: "+f.Status);
                    log.AppendLine(string.Format("{0,-22} Earth launch {1,4:0} s   •   {2:0.000} GB returned{3}",m.Name,rocket.Time,f.Returned,
                        m.Body.Lunar?"   •   lunar transfer and capture":""));
                    File.WriteAllText("verification-results.txt",log.ToString());
                }
                foreach(int boosters in new[]{1,2}){
                    var d=new StackDesign{Booster=boosters,Engine=1};
                    var assisted=new Ascent(Mission.Archive[0],d,600,new SolarSystem());
                    Fly(assisted);
                    var parking=Orbit.FromState(assisted.Mission.Body,assisted.Position,assisted.Velocity,assisted.Time);
                    Assert(assisted.InOrbit,d.Assist.Name+" ascent: "+assisted.Status+" / Pe "+(parking==null?-1:parking.Pe)+" / Ap "+(parking==null?-1:parking.Ap));
                    var a=new Ascent(Mission.Archive[0],d,600,new SolarSystem());
                    a.Auto=false;a.Throttle=0;
                    a.Start();a.Tick(4.4);
                    if(boosters==1){
                        Assert(a.BoosterFuel<d.Assist.Fuel,"Solid thrust should ignore throttle");
                        a.Separate();Assert(!a.BoostersAttached,"Player could not jettison burning solid boosters");
                    }else Assert(a.BoosterFuel==d.Assist.Fuel,"Liquid boosters must obey throttle");
                }
                var custom=new StackDesign{Core=-1,Engine=-1,CoreTanks=0,UpperTanks=0};
                Assert(!custom.Ready&&custom.PadTwr(600,Body.Earth)==0,"An empty rocket must have no pad thrust.");
                custom.Core=0;custom.Engine=0;custom.CoreTanks=1;custom.UpperTanks=1;
                double baseMass=custom.LaunchMass(600);
                custom.CoreTanks++;custom.Booster=1;custom.BoosterPairs=2;
                Assert(custom.Ready && custom.LaunchMass(600)>baseMass &&
                    custom.AssistLoad==StackDesign.Boosters[1].Fuel*2,"Attached parts did not affect the flight model.");
                var burnMission=Mission.Archive[0];
                var fairingCheck=new Ascent(burnMission,new StackDesign(),600,new SolarSystem());
                fairingCheck.Auto=false;fairingCheck.AutoStage=false;fairingCheck.Throttle=0;
                fairingCheck.Start();fairingCheck.Tick(4.1);
                double encapsulatedMass=fairingCheck.TotalMass;
                fairingCheck.JettisonFairing();
                Assert(!fairingCheck.FairingAttached&&fairingCheck.FairingEvent!=null&&
                    Math.Abs(encapsulatedMass-fairingCheck.TotalMass-StackDesign.FairingMass)<.001,
                    "Jettison did not record fairing separation or remove its mass.");
                var fairingRevision=fairingCheck.SeparationRevision;
                fairingCheck.JettisonFairing();
                Assert(fairingCheck.SeparationRevision==fairingRevision,"A detached fairing was jettisoned again.");
                fairingCheck.ReleaseNow();
                Assert(fairingCheck.PayloadEvent!=null&&fairingCheck.PayloadFromStage==0,
                    "Early release did not preserve the stage and event needed by the deployment camera.");
                var manual=new Flight(burnMission,new Vehicle());
                manual.WarpNode();
                double before=manual.Velocity.Length, fuel=manual.Fuel;
                manual.Throttle=1;manual.EngineFiring=true;
                for(int i=0;i<500 && !manual.OnPlan;i++)manual.Tick(.1,0);
                manual.EngineFiring=false;
                Assert(manual.OnPlan && manual.Velocity.Length>before && manual.Fuel<fuel,"Manual burn did not reach the target corridor.");
                double sent=manual.Returned;
                while(!manual.Finished && manual.Time<12000)manual.Tick(120,1);
                Assert(manual.Collected>0 && manual.Returned==sent,"Experiment did not collect locally without downlink.");
                while(!manual.Finished&&!manual.Success&&!manual.DeadlineMissed)manual.Tick(120,3);
                Assert(manual.Success && manual.Returned>sent,"Manual mission did not downlink science.");
                var noEngine=new StackDesign{Core=-1,Engine=-1};
                var stalled=new Ascent(burnMission,noEngine,600,new SolarSystem());
                stalled.Auto=false;stalled.AutoStage=false;stalled.Start();stalled.Tick(12);
                Assert(!stalled.Failed&&!stalled.HasLifted&&Math.Abs(stalled.Altitude)<.001,"A powerless rocket must stay on the pad.");
                stalled.ReleaseNow();
                var detached=new Vehicle();
                for(int slot=1;slot<=6;slot++)detached.Selections[slot]=-1;
                Assert(detached.Wet==0&&detached.FuelDV(0)==0,"Detached equipment still affects mass or propulsion.");
                var released=new Flight(burnMission,detached,stalled);
                released.Tick(10,3);
                Assert(released.Finished && released.Returned==0 && released.Collected==0,"Ground-level payload release must not return science.");
                var unrestricted=new Flight(burnMission,new Vehicle());
                unrestricted.Time=burnMission.Hours*3600+10;
                unrestricted.Tick(1,0);
                Assert(unrestricted.DeadlineMissed&&!unrestricted.Finished,"Missing a scoring deadline should not stop flight.");
                var multiscience=new Vehicle();
                var baseline=MissionAnalysis.Analyze(burnMission,multiscience);
                multiscience.ExtraInstruments[2]=true;
                var heavier=MissionAnalysis.Analyze(burnMission,multiscience);
                var radar=Vehicle.Catalog[5][2];
                Assert(Math.Abs(heavier.MassKg-baseline.MassKg-radar.Mass)<.001 &&
                    heavier.PeakW-baseline.PeakW==radar.Power && heavier.Cost-baseline.Cost==radar.Cost &&
                    heavier.PadTwr<baseline.PadTwr && heavier.RecorderHours<baseline.RecorderHours &&
                    heavier.EstimatedReturnGb<baseline.EstimatedReturnGb,
                    "Additional science must raise mass, power and cost, and reduce thrust-to-weight and recorder capacity in hours.");
                var research=new Flight(burnMission,multiscience,null,false);
                research.WarpNode();research.Insert();
                research.ExperimentEnabled[2]=false;
                research.Tick(3000,1);
                Assert(research.CollectedByInstrument[2]==0,"A disarmed experiment collected data.");
                research.ExperimentEnabled[2]=true;
                while(!research.Success&&!research.DeadlineMissed&&!research.Finished)research.Tick(900,3);
                Assert(research.ReturnedByInstrument[2]>0&&research.QualifiedReturned<=research.ReturnedByInstrument[0]+1e-8,
                    "Secondary science must downlink separately without qualifying as the assigned experiment.");
                Assert(research.Success&&MissionAnalysis.Report(research,true).Score>=80,
                    "The multi-instrument flight did not return the required science by the deadline.");
                var turn=new Flight(burnMission,new Vehicle());
                var coast=new Flight(burnMission,new Vehicle());
                var previous=turn.Velocity;turn.Attitude=3;turn.Throttle=1;turn.EngineFiring=true;
                var direction=turn.BurnDirection;
                turn.Tick(1,0);coast.Tick(1,0);
                Assert(Vector3D.DotProduct(turn.Velocity-coast.Velocity,direction)>0&&
                    Math.Abs(Vector3D.DotProduct(previous,direction))<1e-8,
                    "Normal burn did not apply thrust perpendicular to the orbital plane.");
                double flow=Vehicle.OrbitalThrustN/(9.80665*turn.Vehicle[2].B);
                Assert(Math.Abs(turn.InitialFuel-turn.Fuel-flow)<1e-8,
                    "The maneuver engine did not draw propellant at its rated thrust and Isp.");
                turn.Fuel=.01;turn.Tick(1,0);
                Assert(turn.Fuel==0&&!turn.EngineFiring,"Maneuver engine failed to cut off on propellant exhaustion.");
                var noPropulsion=new Flight(burnMission,detached);
                noPropulsion.EngineFiring=true;
                noPropulsion.Tick(1,0);
                Assert(!noPropulsion.EngineFiring&&noPropulsion.Fuel==0,"Empty spacecraft tried to ignite an engine.");
                var ascendingPayload=new Flight(burnMission,new Vehicle());
                ascendingPayload.Position=new Vector3D(Body.Earth.Radius+80,0,0);
                ascendingPayload.Velocity=Body.Earth.SurfaceVelocity(ascendingPayload.Position)+new Vector3D(1,0,0);
                ascendingPayload.Tick(2,0);
                Assert(!ascendingPayload.Finished&&ascendingPayload.AtmospherePressure>0&&
                    ascendingPayload.Position.Length>Body.Earth.Radius+81,
                    "Deployed payload could not fly upward in the thin atmosphere below 100 km.");
                var descendingPayload=new Flight(burnMission,new Vehicle());
                descendingPayload.Position=new Vector3D(Body.Earth.Radius+20,0,0);
                descendingPayload.Velocity=new Vector3D(-1,0,6);
                descendingPayload.Tick(1,0);
                Assert(descendingPayload.Finished&&descendingPayload.AtmospherePressure>0&&
                    descendingPayload.Status.Contains("aerodynamic load"),
                    "Fast atmospheric entry did not apply a physical loss condition.");
                log.AppendLine("All 15 pad-to-science runs, booster controls and solar integration passed.");
                log.AppendLine("Multi-instrument mass, power, budget, recorder, downlink and scoring checks passed. Normal burn passed.");
                log.AppendLine("Maneuver thrust, empty and depleted engine cutoffs, thin-atmosphere flight, and entry breakup passed.");
                log.AppendLine("Fairing mass, separation events, and early payload release passed.");
                var mars=new MarsFlight(new RoverBuild());
                mars.TickEntry(380);
                Assert(mars.Landed,"Mars baseline did not land: "+mars.Status+" H="+mars.Height+" Fuel="+mars.Fuel+" events="+string.Join("; ",mars.Log));
                Assert(mars.Log.Any(entry=>entry.Contains("Parachute inflated")),"Mars landing skipped the parachute.");
                mars.StartExperiment();
                Assert(!mars.InOperation,"Rover surveyed a distant site without driving there.");
                var target=MarsFlight.Sites[0].Point;
                mars.Heading=Math.Atan2(target.X,target.Y);
                for(int i=0;i<200&&(target-mars.Position).Length>2;i++)mars.Drive(1,0,.1);
                mars.Drive(0,0,1);
                mars.StartExperiment();mars.TickSurface(10);
                Assert(mars.Survey[0,0]&&mars.Stored>0,"Driving and imaging did not record Mars science.");
                mars.Radio=true;
                for(int i=0;i<420 && mars.Returned<.119;i++)mars.TickSurface(5);
                Assert(mars.Returned>=.119,"The Mars relay did not return the survey.");
                var unprotected=new RoverBuild();unprotected.Fitted[1]=0;
                var failedEntry=new MarsFlight(unprotected,125000,1215,5260);failedEntry.TickEntry(380);
                Assert(failedEntry.Failed,"Removing entry hardware did not affect the Mars landing.");
                log.AppendLine("Mars entry, parachute, powered descent, rover traverse, imaging and downlink passed.");
                VerifyHellas(log);
                File.WriteAllText("verification-results.txt",log.ToString());return true;
            }catch(Exception ex){
                log.AppendLine(ex.ToString());File.WriteAllText("verification-results.txt",log.ToString());return false;
            }
        }
        static void VerifyHellas(System.Text.StringBuilder log){
            var site=MarsSiteData.Hellas;
            Assert(site.HasElevationData&&Math.Abs(site.Height(0,0)+8208)<.01,"The Hellas target does not match the MOLA elevation cell.");
            var air=new MarsAtmosphere(site.Pressure,site.Elevation);
            Assert(air.Sample(0).StaticPressurePa>1250&&air.Sample(125000).DensityKgM3<air.Sample(12000).DensityKgM3,
                "Mars pressure and density did not decrease with height.");
            var rover=new RoverBuild();rover.Fitted[1]=2;
            var solar=MarsCruise.LaunchEpoch(MarsCruise.DefaultDepartureDays);
            var m=MarsCruise.EarthParking(solar,MarsCruise.DefaultFlightDays);
            var v=new Vehicle{Stack=new StackDesign{Core=1,Engine=1}};
            var rocket=new Ascent(m,v.Stack,rover.Mass+410+2450,solar);
            Fly(rocket);
            Assert(rocket.InOrbit&&!rocket.Failed,"Hellas Earth ascent failed: "+rocket.Status);
            var parking=new Flight(m,v,rocket,false);
            parking.Tick(MarsCruise.FindDeparture(parking,MarsCruise.DefaultFlightDays),0);
            var plan=MarsCruise.Plan(parking,MarsCruise.DefaultFlightDays,rover);
            Assert(plan!=null&&plan.Path.Length>200,"The Mars transfer planner did not compute a trajectory.");
            var cruise=new MarsCruise(parking,plan,plan.DeltaVelocity,rover.Mass+410,2450,446);
            Assert(cruise.BurnAccepted,"The stock cruise stage did not have sufficient propellant.");
            double departureReserve=MarsCruise.FuelAfterBurn(rover.Mass+410,2450,446,plan.DeltaVelocity.Length);
            Assert(departureReserve>0&&departureReserve<2450&&Math.Abs(cruise.Fuel-departureReserve)<.001,
                "Departure propellant did not match the staged wet mass and calculated burn.");
            var compared=new MarsCruise(parking,plan,plan.DeltaVelocity,rover.Mass+410,2450,446);
            cruise.Tick(12*MarsCruise.Day);compared.Tick(12*MarsCruise.Day);
            var correction=compared.Velocity;correction.Normalize();
            Assert(compared.ApplyCorrection(correction*.006),"The manual correction did not execute.");
            cruise.Tick(6*MarsCruise.Day);compared.Tick(6*MarsCruise.Day);
            Assert((compared.Position-cruise.Position).Length>500&&compared.Fuel<cruise.Fuel,
                "The course correction did not affect actual propagation and propellant.");
            cruise.Tick(370*MarsCruise.Day);
            Assert(cruise.EntryReached&&Math.Abs(cruise.EntryHeightM-125000)<.5&&Math.Abs(cruise.Time-plan.ArrivalTime)<.5,
                "Flight did not reach the predicted atmospheric interface.");
            var descent=new MarsFlight(rover,cruise.EntryHeightM,cruise.DownMps,cruise.HorizontalMps,air);
            double lat=0,lon=0;
            for(int i=0;i<1300&&!descent.Landed&&!descent.Failed;i++){
                if(descent.Height<15000){
                    cruise.GroundCoordinates(descent.Downrange,descent.Crossrange,out lat,out lon);
                    double x=site.EastAt(lon),z=site.NorthAt(lat);
                    double terrain=site.Contains(x,z)?site.Height(x,z):MarsSiteData.GlobalElevation(lat,lon);
                    descent.LandingHeightOffset=terrain-site.Elevation;
                }
                descent.TickEntry(1);
            }
            Assert(descent.Landed&&descent.ChuteAt>=0&&descent.ShieldAt>descent.ChuteAt&&descent.PoweredDescentAt>descent.ShieldAt,
                "Integrated Hellas descent did not land through its hardware events: "+descent.Status);
            Assert(descent.PeakHeatFlux>100000&&descent.PeakPressure>1000,"Entry thermal and aerodynamic loads did not develop.");
            Assert(Math.Abs(lat-site.Latitude)<.24&&Math.Abs(lon-site.Longitude)<.26,"Nominal touchdown fell outside the Hellas target corridor.");
            Assert(descent.InitialFuel==rover.LandingFuel&&descent.Fuel>0&&descent.Fuel<descent.InitialFuel,
                "Landing did not spend the reserved landing tank.");
            double originEast=site.EastAt(lon)-descent.Position.X,originNorth=site.NorthAt(lat);
            descent.TerrainHeight=p=>site.Height(originEast+p.X,originNorth+p.Y)+MarsSiteData.TraverseRelief(p.X,p.Y);
            descent.RadioDelaySeconds=cruise.EarthRange/299792.458;
            for(int i=0;i<2;i++){
                var target=MarsFlight.Sites[i].Point;
                var relative=target-descent.Position;
                descent.Heading=Math.Atan2(relative.X,relative.Y);
                for(int step=0;step<600&&(target-descent.Position).Length>2;step++){
                    descent.Drive(.85,0,.1);descent.TickSurface(.1);
                }
                descent.Drive(0,0,1);
                descent.StartExperiment();descent.TickSurface(12);
                Assert(descent.Survey[i,0],"Rover failed to record a site survey: "+descent.Status);
            }
            Assert(descent.Traverse.Count>15&&descent.Stored>=.23,"The rover drive and recorder did not preserve the traverse and both observations.");
            descent.Radio=true;
            descent.TickSurface(30);
            Assert(descent.InTransit>0&&descent.Returned==0,"Simulated radio packets did not honor light time.");
            for(int i=0;i<2400&&!descent.ObjectiveComplete;i++){
                if(!descent.Contact&&descent.Stored>.001)descent.TickSurface(descent.NextContactSeconds+.3);
                descent.TickSurface(5);
            }
            Assert(descent.ObjectiveComplete,string.Format("Hellas relay returned {0:0.000} GB with {1:0.000} stored, {2:0.000} in flight and {3:N0} Wh.",
                descent.Returned,descent.Stored,descent.InTransit,descent.Charge));
            log.AppendLine(string.Format("Hellas Earth launch, {0:F2} day N-body coast, {1:F2} km/s at 125 km AGL, landing {2:F3}°, {3:F3}°E. Perturbation and radio-delay checks passed.",
                (cruise.Time-plan.BurnTime)/MarsCruise.Day,plan.EntrySpeed,lat,lon));
            log.AppendLine(string.Format("Hellas fuel remaining: {0:N0} kg at departure, {1:N0} kg in the separate landing tank after touchdown. Two driven surveys returned {2:0.000} GB.",
                departureReserve,descent.Fuel,descent.Returned));
        }
    }
}

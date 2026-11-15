using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Game {
        bool hellasMission,autoTrajectory=true,cruisePlanning,cruisePaused;
        bool marsBirdseye;
        Point? marsWaypoint;
        double touchdownEast,touchdownNorth,landedLatitude,landedLongitude;
        bool landingFootprintSet;
        int cruisePackage,launchDay;
        int cruiseRevision;
        double flightDays=245,cruiseRate=864000;
        double correction=4,departureTrim;
        double initialParkingAltitude=300;
        MarsCruise marsCruise;
        MarsTransferPlan marsPlan;
        Vector3D[] cruisePrediction;
        readonly List<Vector3D> cruiseTrack=new List<Vector3D>();
        double lastTrackSample;
        SystemMap interplanetaryMap;
        TextBlock cruiseStatus,cruiseTelemetry;
        Button cruiseCoast,cruiseBurn;
        readonly double[] stageDry={410,680},stageFuel={2450,3900},stageIsp={446,450},stageCost={34,49};
        StackDesign hellasStack;
        static StackDesign HellasStarter(){return new StackDesign{Core=1,Engine=1};}
        double HellasMass {get{return roverBuild.Mass+stageDry[cruisePackage]+stageFuel[cruisePackage];}}
        double HellasCost {get{return roverBuild.Cost+stageCost[cruisePackage]+hellasStack.Cost;}}
        double HellasCruiseDeltaV {get{return MarsCruise.AvailableDeltaV(HellasMass-stageFuel[cruisePackage],stageFuel[cruisePackage],stageIsp[cruisePackage]);}}
        const double HellasBudget=170;
        void HellasBrief(Grid page){
            var split=new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition());
            split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(305)});
            Column(page,split,1);
            var display=new Grid{Background=Dark};
            var site=MarsSiteData.Hellas;
            if(site.ImagePath!=null){
                var photo=new Image{Source=new BitmapImage(new Uri(site.ImagePath)),Stretch=Stretch.UniformToFill};
                RenderOptions.SetBitmapScalingMode(photo,BitmapScalingMode.HighQuality);
                display.Children.Add(photo);
            }
            var caption=new StackPanel();
            Add(caption,Mono("HELLAS PLANITIA",Amber,13),5);
            Add(caption,Mono("NASA MARS TREK  /  VIKING ORBITAL MOSAIC",White,10));
            PlaceHud(display,caption,HorizontalAlignment.Left,VerticalAlignment.Bottom,new Thickness(12,0,0,12));
            Column(split,Frame(display,0),0);
            var brief=new StackPanel();
            Add(brief,Mono("FLIGHT PLAN 01 / "+(Mission.Archive.Length+1),Cyan),8);
            Add(brief,T("Hellas Survey",29,White,true),10);
            Add(brief,T("Land a rover in the deepest MOLA elevation cell and survey the floor of Hellas Planitia.",15,White),18);
            Add(brief,Mono("FLIGHT PLAN",Cyan),5);
            Add(brief,T("Earth launch → parking orbit → Mars transfer → atmospheric entry → rover traverse",13,Muted),16);
            Add(brief,Mono("SCIENCE RETURN",Cyan),5);
            Add(brief,T("Survey two sites and relay 0.20 GB to Earth.",13,White),12);
            Add(brief,Mono("PROGRAM CAP   "+Money(HellasBudget),Cyan),14);
            var mode=Options(new[]{"Auto orbital trajectory","Manual orbital trajectory"},0);
            Add(brief,mode,8);
            Add(brief,Btn("Open vehicle assembly  ›",()=>OpenHellasWithMode(mode.SelectedIndex==0),true));
            Column(split,Frame(Scroll(brief)),1);
        }
        void OpenHellas(){OpenHellasWithMode(true);}
        void OpenHellasWithMode(bool automatic){
            autoTrajectory=automatic;hellasMission=true;roverBuild=new RoverBuild();
            roverBuild.Fitted[1]=2;
            cruisePackage=0;hellasStack=HellasStarter();launchDay=0;flightDays=MarsCruise.DefaultFlightDays;initialParkingAltitude=300;
            departureTrim=0;designName="Hellas Survey";stack=true;rocketCategory=0;
            buildUndo.Clear();roverUndo.Clear();selectedPart="";lastEditorCamera=null;
            OpenHellasRocket();
        }
        void AddHellasDesign(StackPanel choices,Action update){
            Add(choices,Mono("TRAJECTORY CONTROL",Cyan),4);
            var mode=Options(new[]{"Auto orbital trajectory","Manual orbital trajectory"},autoTrajectory?0:1);
            Add(choices,mode,8);
            var plan=new StackPanel();Add(choices,plan,8);
            var day=Range(plan,"LAUNCH DATE OFFSET",0,70,launchDay,1," days");
            var duration=Range(plan,"TRANSFER DURATION",170,310,flightDays,5," days");
            var parking=Range(plan,"PARKING ALTITUDE",210,550,initialParkingAltitude,10," km");
            day.ValueChanged+=(s,e)=>{launchDay=(int)day.Value;update();};
            duration.ValueChanged+=(s,e)=>{flightDays=duration.Value;update();};
            parking.ValueChanged+=(s,e)=>{initialParkingAltitude=parking.Value;update();};
            plan.Visibility=autoTrajectory?Visibility.Collapsed:Visibility.Visible;
            mode.SelectionChanged+=(s,e)=>{
                autoTrajectory=mode.SelectedIndex==0;
                plan.Visibility=autoTrajectory?Visibility.Collapsed:Visibility.Visible;
                update();
            };
            Add(choices,Mono("INTERPLANETARY STAGE",Cyan),4);
            var stages=Options(new[]{"Cruise stage · "+Money(stageCost[0]),"Heavy cruise stage · "+Money(stageCost[1])},cruisePackage);
            stages.SelectionChanged+=(s,e)=>{if(stages.SelectedIndex>=0){cruisePackage=stages.SelectedIndex;update();}};
            Add(choices,stages,12);
        }
        void AddHellasBudget(StackPanel info){
            double balance=HellasBudget-HellasCost;
            Add(info,Mono("MISSION BUDGET",Cyan),5);
            AddBudgetRow(info,"Earth launcher",hellasStack.Cost);
            AddBudgetRow(info,"Cruise stage",stageCost[cruisePackage]);
            AddBudgetRow(info,"Rover & landing systems",roverBuild.Cost);
            var meter=Bar();meter.Value=Math.Min(1,HellasCost/HellasBudget);meter.Foreground=balance<0?Red:Cyan;
            Add(info,meter,5);
            Add(info,T(Money(HellasCost)+" / "+Money(HellasBudget)+" · "+BudgetBalance(balance),12,balance<0?Red:Cyan,true),12);
        }
        void AddHellasPropellant(StackPanel info){
            Add(info,Mono("PROPELLANT BY PHASE",Cyan),5);
            Add(info,T(string.Format("Earth ascent  {0:N0} kg\nMars cruise  {1:N0} kg · {2:0.00} km/s Δv\nMars landing  {3:N0} kg",
                hellasStack.CoreLoad+hellasStack.UpperLoad+hellasStack.AssistLoad,stageFuel[cruisePackage],HellasCruiseDeltaV,roverBuild.LandingFuel),12,White),7);
            Add(info,T("Cruise and landing tanks ride inside the fairing during ascent.",11,Muted),11);
        }
        Button HellasLaunchButton(){
            var button=FlightButton("Launch from Earth   ▶",LaunchHellas,true);
            button.IsEnabled=HellasCost<=HellasBudget;
            return button;
        }
        void OpenHellasRocket(){
            stack=true;selectedPart="";vehicle=new Vehicle{Stack=hellasStack};
            BuildHellasRocket();
        }
        void LaunchHellas(){
            if(HellasCost>HellasBudget)return;
            sandbox=false;flight=null;mars=null;mapFocus=3;
            cruiseRevision++;cruisePlanning=false;
            marsCruise=null;marsPlan=null;cruisePaused=false;landingFootprintSet=false;
            cruisePrediction=null;cruiseTrack.Clear();marsBirdseye=false;marsWaypoint=null;
            StartAscent(true);
        }
        void BeginMarsCruise(){
            if(flight==null||flight.Finished){RefreshLaunch();return;}
            marsCruise=null;cruisePaused=true;
            MarsCruiseScreen();
            if(autoTrajectory)PlanMarsDeparture(true);
        }
        void MarsCruiseScreen(){
            Clear("mars-cruise");
            var grid=new Grid();baseGrid.Children.Add(grid);
            interplanetaryMap=AddNavigationMap(grid,flight.System);
            interplanetaryMap.ZoomToPlanets();
            var header=new DockPanel();
            var back=FlightButton("‹  Workshop",MarsWorkshop);DockPanel.SetDock(back,Dock.Left);header.Children.Add(back);
            var label=T("Hellas survey  /  Interplanetary navigation",18,White,true);label.Margin=new Thickness(12,0,0,0);
            label.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(label);
            PlaceHud(grid,header,HorizontalAlignment.Stretch,VerticalAlignment.Top,new Thickness(10,9,10,0),double.NaN,8);
            var commands=new StackPanel();
            Add(commands,Mono("FLIGHT CONTROL",Cyan),7);
            Add(commands,FlightButton("Plan departure",()=>PlanMarsDeparture(false)),5);
            if(!autoTrajectory)Add(commands,FlightButton("Coast to burn window",CoastToMarsDeparture),5);
            cruiseBurn=FlightButton("Execute departure burn",ExecuteMarsDeparture,true);Add(commands,cruiseBurn,5);
            cruiseCoast=FlightButton("Resume coast",()=>{cruisePaused=!cruisePaused;RefreshMarsCruise();});Add(commands,cruiseCoast,7);
            var rates=new[]{1d,21600,216000,864000};
            var speed=Options(new[]{"1×","6 h / s","2.5 days / s","10 days / s"},Array.IndexOf(rates,cruiseRate));
            speed.SelectionChanged+=(s,e)=>{if(speed.SelectedIndex>=0)cruiseRate=rates[speed.SelectedIndex];};
            Add(commands,Mono("TIME WARP",Muted),4);Add(commands,speed,9);
            if(!autoTrajectory){
                var bias=Range(commands,"DEPARTURE SPEED TRIM",-40,40,departureTrim,1," m/s");
                bias.ValueChanged+=(s,e)=>{departureTrim=bias.Value;RefreshMarsCruise();};
                var dv=Range(commands,"TRIM BURN",.5,35,correction,.5," m/s");
                dv.ValueChanged+=(s,e)=>correction=dv.Value;
                Add(commands,FlightButton("Prograde",()=>TrimMarsCruise(1,0)),4);
                Add(commands,FlightButton("Retrograde",()=>TrimMarsCruise(-1,0)),4);
                Add(commands,FlightButton("Normal",()=>TrimMarsCruise(0,1)),4);
                Add(commands,FlightButton("Anti-normal",()=>TrimMarsCruise(0,-1)),4);
            }
            PlaceHud(grid,commands,HorizontalAlignment.Left,VerticalAlignment.Bottom,new Thickness(12,0,0,13),235);
            var info=new StackPanel();
            cruiseTelemetry=Mono("",White,13);cruiseTelemetry.LineHeight=20;
            cruiseStatus=T("Earth parking orbit. Calculate the departure maneuver.",13,Amber,true);
            Add(info,cruiseTelemetry,8);Add(info,cruiseStatus,0);
            Add(info,Mono("━━  COMPUTED PREDICTION",Cyan),3);
            Add(info,Mono("━━  FLIGHT HISTORY",Amber));
            PlaceHud(grid,info,HorizontalAlignment.Right,VerticalAlignment.Top,new Thickness(0,128,16,0),259);
            RefreshMarsCruise();
        }
        async void PlanMarsDeparture(bool autoBurn){
            if(cruisePlanning||marsCruise!=null||flight==null)return;
            var from=flight;
            int revision=cruiseRevision;
            cruisePlanning=true;
            cruiseStatus.Text="Solving the Mars approach against moving planets…";
            RefreshMarsCruise();
            try{
                double days=autoTrajectory?MarsCruise.DefaultFlightDays:flightDays;
                if(autoBurn){
                    double wait=MarsCruise.FindDeparture(from,days);
                    if(double.IsNaN(wait))throw new InvalidOperationException("Establish a bound Earth parking orbit above the atmosphere before departure.");
                    from.Tick(wait,0);
                }
                if(from.Finished)throw new InvalidOperationException(from.Status);
                var hardware=roverBuild.Copy();
                var plan=await Task.Run(()=>MarsCruise.Plan(from,days,hardware));
                if(screen!="mars-cruise"||flight!=from||revision!=cruiseRevision)return;
                marsPlan=plan;
                cruiseStatus.Text=plan==null?"The solver found no viable departure. Try a different launch date or transfer duration.":"Departure solution available. Inspect propellant and Δv before firing.";
                if(plan!=null&&autoBurn)ExecuteMarsDeparture();
            }catch(InvalidOperationException ex){
                if(screen=="mars-cruise"&&revision==cruiseRevision)cruiseStatus.Text=ex.Message;
            }finally{
                if(revision==cruiseRevision){
                    cruisePlanning=false;
                    if(screen=="mars-cruise")RefreshMarsCruise();
                }
            }
        }
        void CoastToMarsDeparture(){
            if(cruisePlanning||marsCruise!=null)return;
            double wait=MarsCruise.FindDeparture(flight,flightDays);
            if(double.IsNaN(wait)){cruiseStatus.Text="Earth parking orbit is outside the departure envelope.";return;}
            flight.Tick(wait,0);
            marsPlan=null;
            cruiseStatus.Text=string.Format("Coasted {0:0.0} minutes to the departure point. Calculate the burn.",wait/60);
            RefreshMarsCruise();
        }
        void ExecuteMarsDeparture(){
            if(marsPlan==null||marsCruise!=null)return;
            try{
                var delta=marsPlan.DeltaVelocity;
                if(delta.Length>0)delta*=1+departureTrim/(delta.Length*1000);
                var active=new MarsCruise(flight,marsPlan,delta,roverBuild.Mass+stageDry[cruisePackage],stageFuel[cruisePackage],stageIsp[cruisePackage]);
                if(!active.BurnAccepted)throw new InvalidOperationException(active.Status);
                marsCruise=active;
                interplanetaryMap.SetClock(marsCruise.System);
                cruisePrediction=marsPlan.Path;
                cruiseTrack.Add(active.Position);lastTrackSample=active.Time;
                cruisePaused=false;
                cruiseStatus.Text="Departure burn complete. Coasting under Newtonian gravity.";
                if(departureTrim!=0)RefreshCruisePrediction(active);
            }catch(InvalidOperationException ex){cruiseStatus.Text=ex.Message;}
            RefreshMarsCruise();
        }
        void TrimMarsCruise(int along,int across){
            if(cruisePlanning||marsCruise==null||marsCruise.Failed||marsCruise.EntryReached)return;
            var move=marsCruise.Velocity;move.Normalize();
            var normal=Vector3D.CrossProduct(marsCruise.Position,move);normal.Normalize();
            var active=marsCruise;
            if(!active.ApplyCorrection((move*along+normal*across)*(correction/1000))){
                cruiseStatus.Text="Insufficient propellant for correction.";return;
            }
            RefreshCruisePrediction(active);
        }
        async void RefreshCruisePrediction(MarsCruise active){
            int revision=cruiseRevision;
            cruiseStatus.Text="Recalculating the trajectory after the burn.";
            bool wasPaused=cruisePaused;
            cruisePaused=true;cruisePlanning=true;
            try{
                var updated=await Task.Run(()=>active.Predict());
                if(screen!="mars-cruise"||marsCruise!=active||revision!=cruiseRevision)return;
                cruisePrediction=updated==null?null:updated.Path;
                cruiseStatus.Text=updated==null?"No encounter predicted.":
                    string.Format("Predicted minimum Mars altitude {0:N0} km.",updated.ClosestAltitude);
            }finally{
                if(revision==cruiseRevision){cruisePlanning=false;cruisePaused=wasPaused;RefreshMarsCruise();}
            }
        }
        void UpdateMarsCruise(double elapsed){
            if(marsCruise==null||cruisePaused||cruisePlanning||marsCruise.Failed)return;
            marsCruise.Tick(elapsed*Math.Max(1,cruiseRate));
            if(marsCruise.Time-lastTrackSample> MarsCruise.Day*.4){
                cruiseTrack.Add(marsCruise.Position);lastTrackSample=marsCruise.Time;
            }
            if(marsCruise.EntryReached){
                StartIntegratedMarsEntry();
                return;
            }
            RefreshMarsCruise();
        }
        void RefreshMarsCruise(){
            if(screen!="mars-cruise")return;
            var active=marsCruise;
            if(active==null){
                cruiseTelemetry.Text=string.Format("EARTH PARKING ORBIT\nPeriapsis {0:0} km · Apoapsis {1:0} km\nCruise propellant {2:N0} kg",flight.Current==null?0:flight.Current.Pe,flight.Current==null?0:flight.Current.Ap,stageFuel[cruisePackage]);
                if(!autoTrajectory)cruiseTelemetry.Text+="\n"+MarsCruise.Date(flight.Time).ToString("yyyy-MM-dd HH:mm")+" TDB";
                if(marsPlan!=null){
                    double delta=marsPlan.DeltaVelocity.Length+departureTrim/1000;
                    double fuel=stageFuel[cruisePackage];
                    double reserve=MarsCruise.FuelAfterBurn(HellasMass-fuel,fuel,stageIsp[cruisePackage],delta);
                    cruiseTelemetry.Text+=string.Format("\nDeparture Δv {0:N0} m/s\n{1}\nFlight time {2:0} days\nPredicted entry {3:0.00} km/s / {4:0.0}°\nEstimated site offset {5:0} km",
                        delta*1000,reserve>=0?string.Format("{0:N0} kg after departure",reserve):"Insufficient cruise propellant",
                        marsPlan.FlightDays,marsPlan.EntrySpeed,marsPlan.EntryFlightPathAngle,marsPlan.EstimatedLandingErrorKm);
                }
            }else{
                interplanetaryMap.SetClock(active.System);
                var r=active.MarsPosition;
                cruiseTelemetry.Text=string.Format("DISTANCE TO MARS {0:N0} km\nSUN-FRAME SPEED {1:0.00} km/s\nMARS-FRAME SPEED {2:0.00} km/s\nCRUISE {3:N0} / {7:N0} kg\n{8:N0} m/s Δv remaining\nLANDING {9:N0} kg reserved\nTIME {4:0.0} days after launch\nGRAVITY {5:0.000} m/s²\nONE-WAY LIGHT TIME {6:0.0} min",r.Length,active.Velocity.Length,active.MarsVelocity.Length,active.Fuel,(active.Time-launchBaseTime)/86400,active.Acceleration.Length*1000,active.EarthRange/299792.458/60,
                    stageFuel[cruisePackage],active.RemainingDeltaV*1000,roverBuild.LandingFuel);
                if(active.Failed)cruiseStatus.Text=active.Status;
            }
            Vector3D displayed=active==null?flight.System.Positions[3]+flight.Position-flight.System.Positions[0]:active.Position;
            interplanetaryMap.SetMarsFlight(displayed,active!=null?cruisePrediction:marsPlan==null?null:marsPlan.Path,cruiseTrack);
            cruiseCoast.Content=cruisePaused?"Resume coast":"Pause coast";
            cruiseCoast.IsEnabled=active!=null&&!active.Failed&&!cruisePlanning;
            cruiseBurn.IsEnabled=marsPlan!=null&&active==null&&!cruisePlanning;
        }
        double launchBaseTime;
        void StartIntegratedMarsEntry(){
            var site=MarsSiteData.Hellas;
            var environment=new MarsAtmosphere(site.Pressure,site.Elevation);
            mars=new MarsFlight(roverBuild,marsCruise.EntryHeightM,marsCruise.DownMps,marsCruise.HorizontalMps,environment);
            touchdownEast=touchdownNorth=0;landingFootprintSet=false;
            marsBirdseye=false;marsWaypoint=null;
            mars.TerrainHeight=MarsDriveElevation;
            mars.Assist=autoTrajectory;
            mars.RadioDelaySeconds=marsCruise.EarthRange/299792.458;
            marsPaused=true;marsWarp=autoTrajectory?10:4;
            MarsFlightScreen();
        }
        double MarsSurfaceElevation(Point local){
            var site=MarsSiteData.Hellas;
            double x=touchdownEast+local.X,z=touchdownNorth+local.Y;
            if(site.Contains(x,z))return site.Height(x,z);
            return MarsSiteData.GlobalElevation(site.LatitudeAt(z),site.LongitudeAt(x));
        }
        double MarsDriveElevation(Point local){
            return MarsSurfaceElevation(local)+MarsSiteData.TraverseRelief(local.X,local.Y);
        }
        void TrackLandingFootprint(){
            if(marsCruise==null)return;
            marsCruise.GroundCoordinates(mars.Downrange,mars.Crossrange,out landedLatitude,out landedLongitude);
            var site=MarsSiteData.Hellas;
            touchdownEast=site.EastAt(landedLongitude)-mars.Crossrange;
            touchdownNorth=site.NorthAt(landedLatitude);
            mars.LandingHeightOffset=MarsSurfaceElevation(new Point(mars.Crossrange,0))-site.Elevation;
        }
    }
}

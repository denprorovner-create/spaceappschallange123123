using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Game {
        RoverBuild roverBuild=new RoverBuild();
        readonly Stack<RoverBuild> roverUndo=new Stack<RoverBuild>();
        const string RoverInstrumentFormat="Perilune.RoverInstrument";
        MarsFlight mars;
        Scene marsScene;
        MarsTraverse traverse;
        TextBlock marsReadout,marsStatus,marsMode;
        TextBlock descentPrompt;
        ProgressBar marsProgress;
        Border descentTools,scienceTools,traverseFrame,marsTelemetry;
        MarsSurfaceMap surfaceMap;
        Button surfaceView,regionalView;
        Button marsAssist,marsRadio,takeEntry,relayCoast;
        ComboBox marsRates;
        bool marsPaused;
        double marsWarp=1;
        void SandboxMenu(){
            Clear("sandbox-menu");
            Grid grid;Shell("SANDBOX","Choose a flight",Menu,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var earth=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            Add(earth,T("Orbital flight",35,White,true),10);
            Add(earth,T("Build a launch vehicle, choose an Earth or lunar orbit, and return science.",16,Muted),22);
            Add(earth,FlightButton("Plan an orbit",SandboxSetup,true));
            Column(grid,Frame(earth,30),0);
            var marsCard=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            var photo=JezeroImage();
            if(photo!=null){photo.Height=184;photo.Stretch=Stretch.UniformToFill;Add(marsCard,photo,18);}
            Add(marsCard,T("Jezero rover",35,White,true),10);
            Add(marsCard,T("Outfit a rover, descend through Mars’s atmosphere, then drive out to survey the delta.",16,Muted),22);
            Add(marsCard,FlightButton("Practice Mars descent",()=>{hellasMission=false;roverBuild=new RoverBuild();roverUndo.Clear();MarsWorkshop();},true));
            var frame=Frame(marsCard,30);frame.Margin=new Thickness(10,0,0,0);Column(grid,frame,1);
        }
        static Image JezeroImage(){
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","jezero.jpg");
            return File.Exists(path)?new Image{Source=new BitmapImage(new Uri(path)),ToolTip="NASA/JPL-Caltech/ASU/MSSS · Mastcam-Z, PIA24485"}:null;
        }
        void MarsWorkshop(){
            Clear("mars-build");
            Grid grid;Shell(hellasMission?"HELLAS SURVEY / ASSEMBLY":"SANDBOX / MARS","Build your rover",hellasMission?(Action)OpenHellasRocket:SandboxMenu,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(275)});
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(228)});
            var choices=new StackPanel();
            if(hellasMission){
                var tabs=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
                tabs.Children.Add(Btn("Rocket",OpenHellasRocket));tabs.Children.Add(Btn("Rover",()=>{},true));
                Add(choices,tabs,12);
            }
            Scene design=null;
            design=new Scene(()=>design.MarsWorkshopCamera());
            var overview=new StackPanel();
            var instrumentList=new StackPanel();
            Action<int,bool> equip=null;
            Action recalc=()=>{
                overview.Children.Clear();
                if(hellasMission)AddHellasBudget(overview);
                else {
                    Add(overview,Mono("MISSION BUDGET",Cyan),5);
                    double balance=RoverBuild.Budget-roverBuild.Cost;
                    Add(overview,T(Money(roverBuild.Cost)+" / "+Money(RoverBuild.Budget)+"\n"+BudgetBalance(balance),14,balance<0?Red:Cyan,true),13);
                }
                Add(overview,Mono("ENTRY MASS",Cyan),5);
                Add(overview,T(roverBuild.Mass.ToString("N0")+" kg",22,White,true),12);
                Add(overview,T("Parachute drag and engine thrust must slow this mass. More instruments add power draw and science options.",13,Muted),12);
                Add(overview,Mono("FIELD OBJECTIVE",Cyan),4);
                Add(overview,T("Survey two sites and return 0.20 GB. Add a core sampler to cache rock cores.",13,White),11);
                if(hellasMission){
                    var site=MarsSiteData.Hellas;
                    Add(overview,Mono("LANDING SITE",Cyan),4);
                    Add(overview,T(string.Format("{0:F2}° lat, {1:F2}°E\n{2:N0} m MOLA elevation\n{3:N0} Pa modeled reference pressure",site.Latitude,site.Longitude,site.Elevation,site.Pressure),12,White),9);
                    Add(overview,T(string.Format("Lowest MOLA median cell. About {0:F1}° at the target, up to {1:F1}° nearby. Denser air improves braking and raises aerodynamic load.",
                        site.CenterSlopeDegrees,site.NeighborhoodMaxSlopeDegrees),12,Muted),9);
                    AddHellasPropellant(overview);
                    Add(overview,HellasLaunchButton());
                }else Add(overview,FlightButton("Begin Mars descent practice",()=>{
                    mars=new MarsFlight(roverBuild);marsPaused=false;marsWarp=1;MarsFlightScreen();
                },true));
                instrumentList.Children.Clear();
                foreach(int id in roverBuild.Science){
                    int index=id;
                    var row=new DockPanel();
                    var remove=Btn("×",()=>equip(index,false));remove.ToolTip="Detach "+RoverBuild.Parts[6][id].Name;
                    DockPanel.SetDock(remove,Dock.Right);row.Children.Add(remove);
                    var title=T(RoverBuild.Parts[6][id].Name,12,White);title.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(title);
                    Add(instrumentList,row,3);
                }
                design.MarsRoverPreview(roverBuild);
            };
            equip=(index,fitted)=>{
                if(roverBuild.Instruments[index]==fitted)return;
                roverUndo.Push(roverBuild.Copy());roverBuild.Instruments[index]=fitted;recalc();
            };
            for(int i=0;i<6;i++){
                int slot=i;
                Add(choices,Mono(RoverBuild.Families[i].ToUpperInvariant(),Cyan),4);
                var pick=Options(RoverBuild.Parts[i].Select(p=>p.Name+" · "+Money(p.Cost)).ToArray(),roverBuild.Fitted[i]);
                var detail=T(roverBuild.Item(i).Detail,11,Muted);
                pick.SelectionChanged+=(s,e)=>{
                    if(pick.SelectedIndex<0||pick.SelectedIndex==roverBuild.Fitted[slot])return;
                    roverUndo.Push(roverBuild.Copy());
                    roverBuild.Fitted[slot]=pick.SelectedIndex;detail.Text=roverBuild.Item(slot).Detail;recalc();
                };
                Add(choices,pick,3);Add(choices,detail,10);
            }
            Add(choices,Mono("SCIENCE CATALOG",Cyan),5);
            for(int i=0;i<RoverBuild.Parts[6].Length;i++){
                int index=i;var part=RoverBuild.Parts[6][i];
                var card=Btn(part.Name,()=>equip(index,true));
                card.ToolTip=string.Format("{0:N0} kg · {1}\n{2}\nClick or drag onto the rover to attach.",part.Mass,Money(part.Cost),part.Detail);
                Point? dragFrom=null;
                card.PreviewMouseLeftButtonDown+=(s,e)=>dragFrom=e.GetPosition(card);
                card.PreviewMouseLeftButtonUp+=(s,e)=>dragFrom=null;
                card.PreviewMouseMove+=(s,e)=>{
                    if(!dragFrom.HasValue||e.LeftButton!=MouseButtonState.Pressed||(e.GetPosition(card)-dragFrom.Value).Length<5)return;
                    dragFrom=null;DragDrop.DoDragDrop(card,new DataObject(RoverInstrumentFormat,index),DragDropEffects.Copy);
                };
                Add(choices,card,4);
            }
            Add(choices,Mono("FITTED INSTRUMENTS",Cyan),5);Add(choices,instrumentList,9);
            Add(choices,Btn("Undo rover edit",UndoRoverEdit));
            Column(grid,Frame(Scroll(choices),12),0);
            var view=new Grid{Margin=new Thickness(10,0,10,0),Background=new LinearGradientBrush(B(0x472f33).Color,B(0xb17a60).Color,90)};
            view.Children.Add(design);
            view.AllowDrop=true;
            view.DragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(RoverInstrumentFormat)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
            view.Drop+=(s,e)=>{var data=e.Data.GetData(RoverInstrumentFormat);if(data is int)equip((int)data,true);};
            Column(grid,Frame(view,0),1);
            Column(grid,Frame(Scroll(overview),14),2);
            recalc();
        }
        void UndoRoverEdit(){if(roverUndo.Count>0){roverBuild=roverUndo.Pop();MarsWorkshop();}}
        void MarsFlightScreen(){
            Clear("mars-flight");
            var grid=new Grid{ClipToBounds=true};
            baseGrid.Children.Add(grid);
            var marsSky=new LinearGradientBrush(B(0x744f50).Color,B(0xd9a481).Color,90);
            grid.Children.Add(Sky());
            var atmosphere=new Border{Background=marsSky,IsHitTestVisible=false};
            grid.Children.Add(atmosphere);marsAtmosphere=atmosphere;
            marsScene=new Scene(RefreshMars);grid.Children.Add(marsScene.Backdrop);grid.Children.Add(marsScene);
            var header=new DockPanel();
            var back=FlightButton("‹  Workshop",MarsWorkshop);DockPanel.SetDock(back,Dock.Left);header.Children.Add(back);
            var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);
            Add(actions,FlightButton("Pause / resume   P",()=>{marsPaused=!marsPaused;RefreshMars();}));
            Add(actions,FlightButton("Science journal",MarsJournal));
            surfaceView=FlightButton("Satellite view   M",ToggleMarsSurfaceMap);Add(actions,surfaceView);
            regionalView=FlightButton("Traverse map",()=>{surfaceMap.Regional=!surfaceMap.Regional;RefreshMars();});Add(actions,regionalView);
            marsMode=T("",17,White,true);marsMode.Margin=new Thickness(12,0,0,0);marsMode.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(marsMode);
            PlaceHud(grid,header,HorizontalAlignment.Stretch,VerticalAlignment.Top,new Thickness(10,9,10,0),double.NaN,8);
            var hud=new StackPanel();marsReadout=Mono("",White,13);marsStatus=T("",13,Amber,true);marsStatus.MaxWidth=420;marsStatus.TextAlignment=TextAlignment.Center;
            Add(hud,marsReadout,6);Add(hud,marsStatus);
            marsTelemetry=PlaceHud(grid,hud,HorizontalAlignment.Center,VerticalAlignment.Top,new Thickness(0,73,0,0),double.NaN,10);
            marsTelemetry.IsHitTestVisible=false;
            var landing=new StackPanel();
            Add(landing,Mono("MARS DESCENT",Cyan),7);
            descentPrompt=T("",12,Amber);Add(landing,descentPrompt,6);
            takeEntry=FlightButton("Take entry control   ▶",()=>{marsPaused=false;RefreshMars();},true);Add(landing,takeEntry,6);
            marsAssist=FlightButton("",()=>{mars.Assist=!mars.Assist;RefreshMars();},true);Add(landing,marsAssist,6);
            Add(landing,FlightButton("Deploy parachute   F",()=>{mars.DeployChute();RefreshMars();}),5);
            Add(landing,FlightButton("Separate heat shield   H",()=>{mars.JettisonShield();RefreshMars();}),5);
            Add(landing,FlightButton("Start descent engine   E",()=>{mars.StartDescent();RefreshMars();}),5);
            Add(landing,T("↑ / ↓ throttle · Z full · X cut\nA/D entry bank or landing steer",11,Muted),5);
            Add(landing,FlightButton("Restart descent",()=>{
                if(hellasMission)StartIntegratedMarsEntry();
                else {mars=new MarsFlight(roverBuild);marsPaused=false;marsWarp=1;MarsFlightScreen();}
            }));
            descentTools=PlaceHud(grid,landing,HorizontalAlignment.Left,VerticalAlignment.Bottom,new Thickness(12,0,0,12),237);
            var field=new StackPanel();
            Add(field,Mono("ROVER SCIENCE",Cyan),7);
            var scienceSlots=mars.Build.Science.ToArray();
            var selector=Options(scienceSlots.Select(i=>RoverBuild.Parts[6][i].Name).ToArray(),Array.IndexOf(scienceSlots,mars.SelectedScience));
            selector.SelectionChanged+=(s,e)=>{if(selector.SelectedIndex>=0)mars.SelectedScience=scienceSlots[selector.SelectedIndex];};
            Add(field,selector,7);
            Add(field,FlightButton("Run experiment   E",()=>{mars.StartExperiment();RefreshMars();},true),6);
            marsProgress=Bar();Add(field,marsProgress,7);
            marsRadio=FlightButton("",()=>{mars.Radio=!mars.Radio;RefreshMars();});Add(field,marsRadio,6);
            relayCoast=FlightButton("Coast to next relay",()=>{
                if(!mars.Contact&&Math.Abs(mars.Speed)<.1&&!mars.InOperation){
                    mars.TickSurface(mars.NextContactSeconds+.3);
                    RefreshMars();
                }
            });
            Add(field,relayCoast,6);
            Add(field,T("W/S drive · A/D steer\nApproach a marked site and stop to observe.",11,Muted));
            scienceTools=PlaceHud(grid,field,HorizontalAlignment.Left,VerticalAlignment.Bottom,new Thickness(12,0,0,12),243);
            traverse=new MarsTraverse(mars){Width=206,Height=182,ClipToBounds=true};
            traverseFrame=PlaceHud(grid,traverse,HorizontalAlignment.Right,VerticalAlignment.Bottom,new Thickness(0,0,12,12),double.NaN,6);
            var rates=new[]{1,4,10,20,90,300};
            marsRates=Options(rates.Select(rate=>rate+"×").ToArray(),Array.IndexOf(rates,(int)marsWarp));
            marsRates.SelectionChanged+=(s,e)=>{if(marsRates.SelectedIndex>=0)marsWarp=rates[marsRates.SelectedIndex];Focus();};
            var clock=new StackPanel();Add(clock,Mono("TIME WARP",Cyan,10),4);Add(clock,marsRates);
            PlaceHud(grid,clock,HorizontalAlignment.Right,VerticalAlignment.Top,new Thickness(0,73,12,0),85,5);
            surfaceMap=new MarsSurfaceMap(mars,hellasMission?MarsSiteData.Hellas:null,p=>{marsWaypoint=p;RefreshMars();},
                hellasMission?(Func<Point,double>)MarsDriveElevation:null,
                hellasMission?(Func<Point,Point>)(p=>new Point(touchdownEast+p.X,touchdownNorth+p.Y)):null);
            grid.Children.Insert(4,surfaceMap);
            last=watch.Elapsed.TotalSeconds;RefreshMars();Focus();
        }
        Border marsAtmosphere;
        void ToggleMarsSurfaceMap(){
            if(!mars.Landed)return;
            marsBirdseye=!marsBirdseye;RefreshMars();
        }
        void MarsKeys(KeyEventArgs e){
            if(e.OriginalSource is ComboBox||e.OriginalSource is ComboBoxItem)return;
            switch(e.Key){
                case Key.P:if(!e.IsRepeat)marsPaused=!marsPaused;break;
                case Key.M:if(!e.IsRepeat)ToggleMarsSurfaceMap();break;
                case Key.E:if(!e.IsRepeat){if(mars.Landed)mars.StartExperiment();else mars.StartDescent();}break;
                case Key.F:if(!mars.Landed)mars.DeployChute();break;
                case Key.H:if(!mars.Landed)mars.JettisonShield();break;
                case Key.Up:case Key.Down:
                    if(mars.Landed)return;
                    mars.Assist=false;mars.Throttle=Math.Max(0,Math.Min(1,mars.Throttle+(e.Key==Key.Up?.05:-.05)));break;
                case Key.Z:if(mars.Landed)return;mars.Assist=false;mars.Throttle=1;break;
                case Key.X:if(mars.Landed)return;mars.Assist=false;mars.Throttle=0;break;
                case Key.R:if(!e.IsRepeat&&mars.Landed)mars.Radio=!mars.Radio;break;
                default:return;
            }
            e.Handled=true;RefreshMars();
        }
        void UpdateMars(double elapsed){
            if(marsPaused||mars.Failed)return;
            bool wasLanded=mars.Landed;
            bool useControls=IsActive&&!(Keyboard.FocusedElement is ComboBox)&&!(Keyboard.FocusedElement is ComboBoxItem);
            if(!wasLanded){
                mars.LandingSteer=useControls?Axis(Key.D,Key.A):0;
                if(!mars.Engine&&useControls)mars.BankAngle=Math.Max(0,Math.Min(180,mars.BankAngle+mars.LandingSteer*elapsed*26));
                if(hellasMission&&mars.Height<15000)TrackLandingFootprint();
                double closeRangeRate=mars.Assist&&mars.Clearance<650?4:20;
                mars.TickEntry(elapsed*Math.Min(closeRangeRate,marsWarp));
            }
            if(mars.Landed){
                if(!wasLanded){
                    marsWarp=1;marsRates.SelectedIndex=0;
                    if(hellasMission&&!landingFootprintSet){TrackLandingFootprint();landingFootprintSet=true;}
                }
                double drive=useControls?Axis(Key.W,Key.S):0,steer=useControls?Axis(Key.D,Key.A):0;
                if(drive!=0||steer!=0)marsWaypoint=null;
                if(marsWaypoint.HasValue){
                    var delta=marsWaypoint.Value-mars.Position;
                    if(delta.Length<1.5)marsWaypoint=null;
                    else {
                        double heading=Math.Atan2(delta.X,delta.Y);
                        double diff=Math.Atan2(Math.Sin(heading-mars.Heading),Math.Cos(heading-mars.Heading));
                        steer=Math.Max(-1,Math.Min(1,diff*2));
                        drive=Math.Abs(diff)<.38?.70:0;
                    }
                }
                mars.Drive(drive,steer,elapsed);
                mars.TickSurface(elapsed*(drive==0?marsWarp:1));
            }
            RefreshMars();
        }
        static int Axis(Key positive,Key negative){return (Keyboard.IsKeyDown(positive)?1:0)-(Keyboard.IsKeyDown(negative)?1:0);}
        void RefreshMars(){
            if(screen!="mars-flight")return;
            var localTarget=hellasMission&&MarsSiteData.Hellas.Contains(touchdownEast,touchdownNorth);
            marsAtmosphere.Opacity=hellasMission&&!mars.Landed?Math.Exp(-Math.Max(0,mars.Height)/13500):1;
            marsMode.Text=mars.Landed?(hellasMission?(localTarget?"Hellas Planitia":"Mars alternate landing"):"Jezero Crater")+"  /  Surface operations":"Mars arrival  /  Entry and descent";
            surfaceView.Visibility=mars.Landed?Visibility.Visible:Visibility.Collapsed;
            surfaceView.Content=marsBirdseye?"3D rover view   M":"Satellite view   M";
            surfaceMap.Visibility=marsBirdseye&&mars.Landed?Visibility.Visible:Visibility.Collapsed;
            regionalView.Visibility=marsBirdseye&&mars.Landed&&surfaceMap.HasRegional?Visibility.Visible:Visibility.Collapsed;
            regionalView.Content=surfaceMap.Regional?"Traverse map":"Orbital mosaic";
            marsScene.Visibility=surfaceMap.Visibility==Visibility.Visible?Visibility.Collapsed:Visibility.Visible;
            marsTelemetry.HorizontalAlignment=mars.Landed?HorizontalAlignment.Right:HorizontalAlignment.Center;
            marsTelemetry.Width=mars.Landed?259:double.NaN;
            marsTelemetry.Margin=mars.Landed?new Thickness(0,141,12,0):new Thickness(0,73,0,0);
            marsStatus.Text=(marsPaused?"Paused · ":"")+mars.Status;
            marsStatus.Foreground=mars.Failed?Red:Amber;
            takeEntry.Visibility=marsPaused&&!mars.Landed&&!mars.Failed?Visibility.Visible:Visibility.Collapsed;
            marsAssist.Content=mars.Assist?"Landing assist   ON":"Landing assist   OFF";
            descentPrompt.Text=mars.Engine?(mars.Fuel>0?"Engine live · aim for under 3.7 m/s down.":"Descent propellant depleted."):mars.DeploymentMessage;
            var site=MarsFlight.Sites[mars.ClosestSite];
            marsReadout.Text=mars.Landed?
                string.Format("{0:0.0} m/s   ·   {1:0} m traversed\n{2} · {3:0} m\n{4:0} Wh   |   {5:0.000} GB stored   |   {6:0.000} GB sent\nRelay {7}   ·   {8}× operations\n{9}",mars.Speed,mars.Distance,site.Name,(site.Point-mars.Position).Length,mars.Charge,mars.Stored,mars.Returned,mars.Contact?"in view":"below horizon",marsWarp,
                    mars.ObjectiveComplete?"Survey goal reached":mars.SurveyedSites+" / 2 sites surveyed · return 0.20 GB"):
                string.Format("{0:N0} m AGL   ·   {1:0} m/s   ·   Mach {7:0.0}\n{2:0} m/s down   ·   {3:0} m/s across\nLanding {4:0} / {13:0} kg   ·   {5:0}% throttle\nStatic {8} Pa   ·   dynamic {6:0.00} kPa\n{9:0} K   ·   {10} kg/m³\nHeating {11:0.0} kW/m²   ·   Drag {12:0.0} m/s²",
                    mars.Clearance,mars.SpeedAir,mars.Fall,mars.HorizontalSpeed,mars.Fuel,mars.Throttle*100,mars.Pressure/1000,mars.Mach,
                    mars.StaticPressure.ToString(mars.StaticPressure<1?"0.00E+0":"0.00"),mars.Temperature,mars.Density.ToString(mars.Density<.0001?"0.00E+0":"0.00000"),
                    mars.HeatFlux/1000,mars.EntryAccel,mars.InitialFuel);
            if(mars.Landed&&mars.RadioDelaySeconds>0)
                marsReadout.Text+=string.Format("\n{0:0.000} GB in transit · {1:0.0} min light time",mars.InTransit,mars.RadioDelaySeconds/60);
            if(mars.Landed&&!mars.Contact)marsReadout.Text+=string.Format("\nNext relay pass in {0:0.0} min",mars.NextContactSeconds/60);
            if(mars.Landed&&hellasMission)
                marsReadout.Text+=string.Format("\nTouchdown {0:F3}° lat · {1:F3}°E\n{2:N0} m / MOLA · {3:0.0} km from target",landedLatitude,landedLongitude,MarsSurfaceElevation(mars.Position),
                    new Vector(touchdownEast+mars.Crossrange,touchdownNorth).Length/1000);
            marsRadio.Content=mars.Radio?"Stop downlink   R":"Downlink science   R";
            relayCoast.IsEnabled=!mars.Contact&&Math.Abs(mars.Speed)<.1&&!mars.InOperation;
            marsProgress.Value=mars.Progress;
            descentTools.Visibility=mars.Landed?Visibility.Collapsed:Visibility.Visible;
            scienceTools.Visibility=mars.Landed?Visibility.Visible:Visibility.Collapsed;
            traverseFrame.Visibility=mars.Landed&&!marsBirdseye?Visibility.Visible:Visibility.Collapsed;
            if(surfaceMap.Visibility==Visibility.Visible)surfaceMap.Update(marsWaypoint);
            else marsScene.MarsView(mars,hellasMission?MarsSiteData.Hellas:null,hellasMission?(Func<Point,double>)MarsSurfaceElevation:null,
                hellasMission?marsCruise:null,new Point(touchdownEast,touchdownNorth));
            traverse.InvalidateVisual();
        }
        void MarsJournal(){
            Clear("mars-journal");
            Grid grid;Shell("MARS SCIENCE",hellasMission?"Hellas field journal":"Jezero field journal",MarsFlightScreen,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
            var log=new StackPanel();
            Add(log,T(string.Format("{0:0.000} GB delivered   ·   {1} samples cached",mars.Returned,mars.Samples),21,White,true),10);
            Add(log,T(mars.ObjectiveComplete?"Survey goal reached":mars.SurveyedSites+" / 2 sites surveyed · 0.20 GB return goal",14,mars.ObjectiveComplete?Cyan:Muted),8);
            double cost=hellasMission?HellasCost:mars.Build.Cost;
            double cap=hellasMission?HellasBudget:RoverBuild.Budget;
            Add(log,T(Money(cost)+" committed · "+BudgetBalance(cap-cost),13,Cyan),14);
            for(int i=0;i<MarsFlight.Sites.Length;i++){
                var observed=mars.Build.Science.Where(n=>mars.Survey[i,n]).Select(n=>RoverBuild.Parts[6][n].Name).ToArray();
                if(observed.Length==0)continue;
                Add(log,T(MarsFlight.Sites[i].Name,17,White,true),3);
                Add(log,T(string.Join(" · ",observed)+"\n"+MarsFlight.Sites[i].Geology,13,Muted),12);
            }
            if(mars.Log.Count==0)Add(log,T("Fly the descent to begin collecting observations.",14,Muted));
            foreach(var line in mars.Log.Reverse<string>())Add(log,T(line,12,Muted),5);
            Column(grid,Frame(Scroll(log),16),0);
            var reference=new StackPanel();
            if(hellasMission){
                Add(reference,T("Hellas basin measurements",22,White,true),9);
                Add(reference,T(string.Format("MOLA target elevation {0:N0} m\nPeak dynamic pressure {1:0.00} kPa\nPeak modeled heating {2:0.0} kW/m²\nModeled reference pressure {3:0} Pa\n\nRock interpretations are simulated. MOLA provides the regional elevations.",
                    MarsSiteData.Hellas.Elevation,mars.PeakPressure/1000,mars.PeakHeatFlux/1000,MarsSiteData.Hellas.Pressure),14,Muted));
                Add(reference,T("Weather readings logged: "+Enumerable.Range(0,MarsFlight.Sites.Length).Count(i=>mars.Survey[i,3]),13,White),8);
            }else{
                Add(reference,T("Jezero delta reference",22,White,true),9);
                var image=JezeroImage();if(image!=null)Add(reference,image,9);
                Add(reference,T("Mastcam-Z, Perseverance · PIA24485\nNASA/JPL-Caltech/ASU/MSSS",12,Muted));
            }
            Column(grid,Frame(Scroll(reference),16),1);
        }
    }
    sealed class MarsTraverse : FrameworkElement {
        readonly MarsFlight flight;
        public MarsTraverse(MarsFlight flight){this.flight=flight;IsHitTestVisible=false;}
        Point Project(Point p){return new Point(ActualWidth*.41+p.X*1.35,ActualHeight*.84-p.Y*1.63);}
        protected override void OnRender(DrawingContext dc){
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(38,29,30)),null,new Rect(RenderSize));
            var center=Project(new Point());
            var pen=new Pen(new SolidColorBrush(Color.FromRgb(126,87,77)),1);
            for(int i=1;i<=4;i++)dc.DrawEllipse(null,pen,center,19*i,17*i);
            foreach(var s in MarsFlight.Sites){
                var p=Project(s.Point);dc.DrawEllipse(Brushes.SandyBrown,null,p,4,4);
                var label=new FormattedText(s.Name,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,Brushes.Bisque,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label,new Point(p.X+6,p.Y-11));
            }
            var rover=Project(flight.Position);
            dc.DrawEllipse(Brushes.White,null,rover,4.5,4.5);
            dc.DrawLine(new Pen(Brushes.White,1.3),rover,rover+new Vector(Math.Sin(flight.Heading)*13,-Math.Cos(flight.Heading)*13));
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Game {
        DesignAnalysis comparison;
        Tuple<bool,double,double,double> lastEditorCamera;
        TextBlock orbitSummary, orbitOperations;
        readonly TextBlock[] experimentTelemetry=new TextBlock[5];
        Navball orbitBall;
        Button orbitViewButton, orbitToolsButton;
        Border orbitTools;
        StackPanel orbitLegend;
        TextBlock transferLegend;
        Grid orbitMapLayer;
        SystemMap orbitMap;
        int mapFocus=3;
        bool showOrbitTools;
        Button[] orbitDirectionButtons;
        Action refreshHellasLauncher;
        static Border PlaceHud(Grid grid,UIElement child,HorizontalAlignment horizontal,VerticalAlignment vertical,Thickness margin,double width=double.NaN,double pad=11) {
            var frame=HudFrame(child,pad);
            frame.HorizontalAlignment=horizontal;frame.VerticalAlignment=vertical;
            frame.Margin=margin;frame.Width=width;grid.Children.Add(frame);
            return frame;
        }
        void Build() {
            if(hellasMission&&screen=="mars-rocket"){BuildHellasRocket();return;}
            BuildAssembly(false);
        }
        void BuildHellasRocket(){
            hellasStack=vehicle.Stack;
            BuildAssembly(true);
        }
        void BuildAssembly(bool marsRocket) {
            refreshHellasLauncher=null;
            if((screen=="build"||screen=="mars-rocket")&&scene!=null&&lastEditorCamera!=null)
                lastEditorCamera=Tuple.Create(lastEditorCamera.Item1,scene.Yaw,scene.Pitch,scene.Zoom);
            Clear(marsRocket?"mars-rocket":"build");flight=null;
            var root=new Grid{Background=Dark};
            root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            root.RowDefinitions.Add(new RowDefinition());
            baseGrid.Children.Add(root);
            var top=new DockPanel{Margin=new Thickness(10,8,10,8)};
            root.Children.Add(top);
            Action returnToList=marsRocket?(Action)(()=>Archive(-1)):sandbox?(Action)Menu:()=>Archive(Array.IndexOf(Mission.Archive,mission));
            var back=FlightButton(!marsRocket&&sandbox?"‹  Menu":"‹  Missions",returnToList);
            DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);
            var actions=new StackPanel{Orientation=Orientation.Horizontal};
            DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
            Button launchAction;
            if(marsRocket)launchAction=HellasLaunchButton();
            else{
                Add(actions,FlightButton("Save",SaveVehicle));Add(actions,FlightButton("Load",LoadVehicle));
                Add(actions,FlightButton(sandbox?"Edit flight plan":"Flight plan",()=>{if(sandbox)SandboxSetup(true);else FlightScreen(false);}));
                launchAction=FlightButton(!sandbox&&vehicle.Cost>mission.Budget?"Launch test   ▶":"Launch   ▶",()=>StartAscent(true),true);
                launchAction.ToolTip=!sandbox&&vehicle.Cost>mission.Budget?"This flight can return science. The preset mission still fails its budget requirement.":"Roll out to the launch pad.";
            }
            Add(actions,launchAction);
            if(!marsRocket){
                var funds=AssemblyBudgetHeader();
                DockPanel.SetDock(funds,Dock.Right);top.Children.Add(funds);
            }
            var name=new StackPanel{Margin=new Thickness(14,0,10,0)};
            Add(name,Mono((marsRocket?"HELLAS SURVEY":mission.Name.ToUpperInvariant())+"   /   VEHICLE ASSEMBLY",Cyan),2);
            var nameEdit=new TextBox{Text=designName,MaxLength=48,Background=Dark,Foreground=White,BorderBrush=Line,FontSize=15,Padding=new Thickness(4,2,4,2)};
            nameEdit.TextChanged+=(s,e)=>designName=nameEdit.Text;
            Add(name,nameEdit);top.Children.Add(name);
            var body=new Grid{ClipToBounds=true};Grid.SetRow(body,1);root.Children.Add(body);
            body.ColumnDefinitions.Add(new ColumnDefinition());
            body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(262)});
            var view=new Grid{Background=B(0x41474b),ClipToBounds=true};
            body.Children.Add(view);
            scene=new Scene(()=>scene.AssemblyView(stack));
            scene.Reset(true);
            if(lastEditorCamera!=null&&lastEditorCamera.Item1==stack){
                scene.Yaw=lastEditorCamera.Item2;scene.Pitch=lastEditorCamera.Item3;scene.Zoom=lastEditorCamera.Item4;
            }
            if(marsRocket){scene.MarsPayload=roverBuild;scene.MarsCruisePackage=cruisePackage;}
            scene.Assembly(vehicle,stack);view.Children.Add(scene);
            lastEditorCamera=Tuple.Create(stack,scene.Yaw,scene.Pitch,scene.Zoom);
            scene.SelectedPart=selectedPart;
            scene.PartPicked=key=>{selectedPart=key??"";ShowSelected();};
            AttachDrop(view);
            var dock=new StackPanel{Margin=new Thickness(10,10,0,10)};
            Add(dock,Mono("PART CATALOG",Cyan),10);
            RenderParts(dock);
            var parts=HudFrame(Scroll(dock),8);
            parts.Margin=new Thickness(8,10,0,10);parts.Width=222;parts.HorizontalAlignment=HorizontalAlignment.Left;
            view.Children.Add(parts);
            var selected=new StackPanel();
            partName=T("",14,White,true);partDetail=T("",11,Muted);partDelta=Mono("",Cyan);
            Add(selected,partName,4);Add(selected,partDetail,5);Add(selected,partDelta);
            var inspector=PlaceHud(view,selected,HorizontalAlignment.Right,VerticalAlignment.Bottom,new Thickness(0,0,12,12),238);
            inspector.IsHitTestVisible=false;
            ShowSelected();
            var stats=new StackPanel();
            if(marsRocket){
                var systems=new StackPanel();
                refreshHellasLauncher=()=>{
                    systems.Children.Clear();
                    AddHellasBudget(systems);
                    Add(systems,Mono("LIFTOFF",Cyan),4);
                    Add(systems,T(string.Format("{0:N0} kg wet · TWR {1:0.00}\n{2:N0} kg beneath the fairing",
                        hellasStack.LaunchMass(HellasMass),hellasStack.PadTwr(HellasMass,Body.Earth),HellasMass),12,White),8);
                    AddHellasPropellant(systems);
                    launchAction.IsEnabled=HellasCost<=HellasBudget;
                };
                Add(stats,systems,9);
                AddHellasDesign(stats,refreshHellasLauncher);refreshHellasLauncher();
                Column(body,Frame(Scroll(stats),14),1);
                return;
            }
            Add(stats,T(mission.Objective,15,White,true),8);
            Add(stats,Mono((mission.Body.Lunar?"EARTH LAUNCH  →  MOON   ":"EARTH   ")+OrbitLabel(mission),Cyan),14);
            Add(stats,AssemblyCostBreakdown(),13);
            if(!sandbox&&vehicle.Cost>mission.Budget)Add(stats,T("Test flight available. Reduce cost to qualify for mission success.",12,Red,true),9);
            Add(stats,Mono("VEHICLE TOTALS",Cyan),4);
            buildSummary=Mono("",White,11);Add(stats,buildSummary,10);
            UpdateBuildSummary();
            var a=MissionAnalysis.Analyze(mission,vehicle);
            Add(stats,Mono("ON THE PLANNED ORBIT",Cyan),5);
            Add(stats,T(string.Format("Contact {0:0}% · science window {1:0}%\nLongest eclipse ~{2:0} min\nProjected return {3:0.00} GB\nGoal {4:0.00} GB in {5:0} h",a.ContactFraction*100,a.ScienceFraction*100,a.EclipseMinutes,a.EstimatedReturnGb,mission.Dose,mission.Hours),12,White),10);
            var issues=vehicle.Issues(mission);
            if(issues.Count>0){Add(stats,Mono("FLIGHT ADVISORIES",Amber),5);foreach(var issue in issues)Add(stats,T(issue,11,Amber),4);}
            Add(stats,FlightButton("Trade study",()=>TradeStudy()),8);
            Add(stats,FlightButton("Solar system",()=>OpenSystemMap(Build,new SolarSystem())),8);
            Column(body,Frame(Scroll(stats),14),1);
        }
        static string Money(double millions){
            return "$"+Math.Abs(millions).ToString("0.#")+"M";
        }
        static string BudgetBalance(double balance){
            return Money(balance)+(balance<0?" over budget":" remaining");
        }
        Border AssemblyBudgetHeader(){
            var row=new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});
            row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(82)});
            double balance=mission.Budget-vehicle.Cost;
            Column(row,BudgetMetric("COMMITTED",vehicle.Cost,White),0);
            Column(row,BudgetMetric(sandbox?"TARGET":"MISSION CAP",mission.Budget,White),1);
            Column(row,BudgetMetric(balance<0?"OVER BUDGET":"REMAINING",balance,balance<0?Red:Cyan),2);
            var border=HudFrame(row,7);
            border.Margin=new Thickness(0,0,8,0);
            border.ToolTip=sandbox?"Sandbox uses the budget as a planning target.":"The cap counts toward mission success. You can fly any design.";
            return border;
        }
        static StackPanel BudgetMetric(string label,double value,Brush color){
            var metric=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            var heading=Mono(label,Muted,10);
            heading.TextWrapping=TextWrapping.NoWrap;
            Add(metric,heading,2);
            Add(metric,T(Money(value),15,color,true));
            return metric;
        }
        StackPanel AssemblyCostBreakdown(){
            var breakdown=new StackPanel();
            Add(breakdown,Mono("MISSION BUDGET",Cyan),5);
            double rocket=vehicle.Stack.Cost;
            double ground=vehicle[6].Cost;
            double committed=vehicle.Cost;
            AddBudgetRow(breakdown,"Launch vehicle",rocket);
            AddBudgetRow(breakdown,"Spacecraft & instruments",committed-rocket-ground);
            AddBudgetRow(breakdown,"Ground network",ground);
            double balance=mission.Budget-committed;
            var meter=Bar();
            meter.Height=5;
            meter.Foreground=balance<0?Red:Cyan;
            meter.Value=Math.Min(1,committed/mission.Budget);
            meter.Margin=new Thickness(0,5,0,0);
            Add(breakdown,meter,4);
            Add(breakdown,T(Money(committed)+" of "+Money(mission.Budget)+" · "+BudgetBalance(balance),11,balance<0?Red:Cyan,true));
            return breakdown;
        }
        static void AddBudgetRow(StackPanel breakdown,string label,double value){
            var row=new DockPanel();
            var amount=Mono(Money(value),White,11);
            DockPanel.SetDock(amount,Dock.Right);
            row.Children.Add(amount);
            row.Children.Add(T(label,12,Muted));
            Add(breakdown,row,4);
        }
        void TradeStudy(){
            var a=MissionAnalysis.Analyze(mission,vehicle);
            Clear("trade");
            Grid grid;Shell("VEHICLE ASSEMBLY","Trade study",Build,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var left=new StackPanel();
            Add(left,T("Design margins",22,White,true),12);
            Add(left,T(Money(a.Cost)+" of "+Money(mission.Budget)+" committed · "+BudgetBalance(a.BudgetMargin),14,a.BudgetMargin<0?Red:Cyan,true),8);
            Add(left,T(string.Format("Payload {0:0} kg\nPad thrust-to-weight {1:0.00} · launch Δv {2:0.00} km/s\nOrbital Δv {3:0} m/s · insertion estimate {4:0} m/s\nAverage sunlight generation {5:0} W · estimated demand {6:0} W",vehicle.Wet,a.PadTwr,a.AscentDeltaV,a.OrbitalDeltaV,Flight.PlannedDv(mission)*1000,a.MeanPowerW,a.MeanLoadW),14),14);
            foreach(var advice in a.Advice)Add(left,T(advice,13,Muted),11);
            Column(grid,Frame(Scroll(left),18),0);
            var right=new StackPanel();
            Add(right,T("Compare two designs",22,White,true),12);
            Add(right,FlightButton("Remember this design as baseline",()=>{comparison=a;TradeStudy();},true),12);
            if(comparison==null)Add(right,T("Record a baseline, change the vehicle, and return here to compare the results.",14,Muted));
            else {
                CompareLine(right,"Liftoff mass",comparison.MassKg,a.MassKg,"kg","N0");
                Add(right,Mono("TOTAL COST",Cyan),3);
                Add(right,T(Money(comparison.Cost)+"  →  "+Money(a.Cost),16,White,true),12);
                CompareLine(right,"Pad thrust-to-weight",comparison.PadTwr,a.PadTwr,"","0.00");
                CompareLine(right,"Ascent Δv",comparison.AscentDeltaV,a.AscentDeltaV,"km/s","0.00");
                CompareLine(right,"Power margin in sunlight",comparison.PowerMarginW,a.PowerMarginW,"W","0");
                CompareLine(right,"Battery at peak load",comparison.BatteryMinutes,a.BatteryMinutes,"min","0");
                CompareLine(right,"Projected data returned",comparison.EstimatedReturnGb,a.EstimatedReturnGb,"GB","0.00");
            }
            Column(grid,Frame(Scroll(right),18),1);
        }
        static void CompareLine(StackPanel list,string label,double baseline,double current,string unit,string format){
            Add(list,Mono(label.ToUpperInvariant(),Cyan),3);
            Add(list,T(baseline.ToString(format)+"  →  "+current.ToString(format)+" "+unit,16,White,true),12);
        }
        void SaveVehicle(){
            var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Perilune design (*.perilune)|*.perilune",DefaultExt=".perilune",FileName=string.Concat(designName.Split(Path.GetInvalidFileNameChars()))};
            if(dialog.ShowDialog(this)!=true)return;
            var s=vehicle.Stack;
            try{
                File.WriteAllLines(dialog.FileName,new[]{"Perilune design 1",designName, string.Join(",",vehicle.Selections),
                    string.Join(",",vehicle.ExtraInstruments.Select(x=>x?"1":"0")),
                    string.Join(",",new[]{s.Core,s.Engine,s.Booster,s.CoreTanks,s.UpperTanks,s.BoosterPairs}),
                    s.Load.ToString("R",CultureInfo.InvariantCulture)});
            }catch(IOException ex){MessageBox.Show(this,ex.Message,"Design could not be saved");}
            catch(UnauthorizedAccessException ex){MessageBox.Show(this,ex.Message,"Design could not be saved");}
        }
        void LoadVehicle(){
            var d=new Microsoft.Win32.OpenFileDialog{Filter="Perilune design (*.perilune)|*.perilune"};
            if(d.ShowDialog(this)!=true)return;
            try{
                var lines=File.ReadAllLines(d.FileName);
                if(lines.Length!=6||lines[0]!="Perilune design 1")throw new FormatException("This is not a supported design file.");
                var choices=lines[2].Split(',').Select(int.Parse).ToArray();
                var flags=lines[3].Split(',').Select(int.Parse).ToArray();
                var stackParts=lines[4].Split(',').Select(int.Parse).ToArray();
                double load=double.Parse(lines[5],CultureInfo.InvariantCulture);
                if(choices.Length!=7||flags.Length!=5||stackParts.Length!=6||
                   choices.Skip(1).Where((choice,i)=>choice< -1||choice>=Vehicle.Catalog[i+1].Length).Any()||flags.Any(x=>x<0||x>1)||
                   stackParts[0]<-1||stackParts[0]>=StackDesign.Cores.Length||stackParts[1]<-1||stackParts[1]>=StackDesign.Upper.Length||
                   stackParts[2]<0||stackParts[2]>=StackDesign.Boosters.Length||stackParts.Skip(3).Take(2).Any(x=>x<0||x>6)||
                   stackParts[5]<1||stackParts[5]>2||double.IsNaN(load)||load<.1||load>1)throw new FormatException("The design contains an invalid part or load.");
                var loaded=new Vehicle{Stack=new StackDesign{Core=stackParts[0],Engine=stackParts[1],Booster=stackParts[2],
                    CoreTanks=stackParts[3],UpperTanks=stackParts[4],BoosterPairs=stackParts[5],Load=load}};
                Array.Copy(choices,loaded.Selections,7);
                for(int i=0;i<flags.Length;i++)loaded.ExtraInstruments[i]=flags[i]==1;
                RememberDesign();vehicle=loaded;selectedPart="";designName=lines[1];Build();
            }catch(Exception ex){
                if(!(ex is IOException||ex is FormatException||ex is UnauthorizedAccessException||ex is OverflowException))throw;
                MessageBox.Show(this,ex.Message,"Design could not be loaded");
            }
        }
        void FlightScreen(bool active){
            Clear("flight");
            if(!active){flight=null;preview=mission.Target;follow=false;showOrbitTools=false;}
            var grid=new Grid{ClipToBounds=true};
            baseGrid.Children.Add(grid);grid.Children.Add(Sky());
            scene=new Scene(RefreshFlight);scene.Reset(false);grid.Children.Add(scene.Backdrop);grid.Children.Add(scene);
            orbitMapLayer=new Grid();grid.Children.Add(orbitMapLayer);
            orbitMap=AddNavigationMap(orbitMapLayer,active?flight.System:new SolarSystem());
            var top=new DockPanel();
            var back=FlightButton("‹  Assembly",Build);DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);
            var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);top.Children.Add(actions);
            if(active){
                orbitViewButton=FlightButton("Map   M",()=>Follow(!follow));Add(actions,orbitViewButton);
                orbitToolsButton=FlightButton("Maneuvers",ToggleOrbitTools);Add(actions,orbitToolsButton);
                Add(actions,FlightButton("Report",ShowReport));
            }else Add(actions,FlightButton("Launch   ▶",()=>StartAscent(true),true));
            var title=new StackPanel{Margin=new Thickness(12,0,0,0)};
            Add(title,Mono(mission.Name.ToUpperInvariant(),Cyan),2);
            viewHeading=T("",15,White,true);Add(title,viewHeading);top.Children.Add(title);
            PlaceHud(grid,top,HorizontalAlignment.Stretch,VerticalAlignment.Top,new Thickness(10,9,10,0),double.NaN,8);
            var situation=new StackPanel();
            phaseText=Mono("",Cyan);orbitSummary=Mono("",White,12);status=T("",12,Amber);
            Add(situation,phaseText,5);Add(situation,orbitSummary,6);Add(situation,status);
            PlaceHud(grid,situation,HorizontalAlignment.Center,VerticalAlignment.Top,new Thickness(0,72,0,0),double.NaN,9).IsHitTestVisible=false;
            var legend=new StackPanel();
            guideLegend=Mono("━━  ASSIGNED ORBIT",Cyan);flownLegend=Mono("━━  CURRENT ORBIT",Amber);
            Add(legend,guideLegend,4);
            if(active){
                Add(legend,flownLegend,3);
                transferLegend=Mono("━━  PLANNED TRANSFER",Cyan);
                Add(legend,transferLegend,3);
                Add(legend,Mono("●  SPACECRAFT POSITION",White),3);
            }
            Add(legend,Mono("DRAG TO ROTATE · SCROLL TO ZOOM",Muted,10));
            orbitLegend=legend;
            PlaceHud(grid,legend,HorizontalAlignment.Left,VerticalAlignment.Bottom,new Thickness(12,0,0,12),double.NaN,8).IsHitTestVisible=false;
            if(!active){
                var brief=new StackPanel();
                Add(brief,T(mission.Objective,15,White,true),12);Brief(brief,mission);
                Add(brief,Mono(string.Format("PERIOD {0:0.0} min · INSERTION {1:0} m/s",preview.Period/60,Flight.PlannedDv(mission)*1000),Cyan));
                PlaceHud(grid,brief,HorizontalAlignment.Right,VerticalAlignment.Center,new Thickness(0,0,12,0),245);
            }else{
                var f=flight;
                var ops=new StackPanel();
                Add(ops,Mono("SCIENCE & DOWNLINK",Cyan),7);
                orbitOperations=Mono("",White,11);Add(ops,orbitOperations,7);
                Add(ops,Mono("BATTERY / QUALIFYING RETURN",Muted,10),4);
                powerBar=Bar();dataBar=Bar();Add(ops,powerBar,5);Add(ops,dataBar,8);
                foreach(int item in f.Vehicle.InstalledInstruments){
                    int i=item;
                    var checkbox=Check(Vehicle.Catalog[5][i].Name+(i==mission.Instrument?" ★":""),f.ExperimentEnabled[i],enabled=>{f.ExperimentEnabled[i]=enabled;RefreshFlight();});
                    Add(ops,checkbox,1);experimentTelemetry[i]=Mono("",Muted,10);Add(ops,experimentTelemetry[i],5);
                }
                observeButton=FlightButton("Start science   E",()=>{science=!science;RefreshFlight();},true);
                transmitButton=FlightButton("Transmit to Earth   R",()=>{tx=!tx;RefreshFlight();});
                ops.Children.Insert(2,observeButton);
                ops.Children.Insert(3,transmitButton);
                var operationsFrame=PlaceHud(grid,Scroll(ops),HorizontalAlignment.Right,VerticalAlignment.Bottom,new Thickness(0,0,12,12),239);
                operationsFrame.MaxHeight=490;
                grid.SizeChanged+=(s,e)=>operationsFrame.MaxHeight=Math.Min(490,Math.Max(280,grid.ActualHeight-178));
                var controls=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,12)};
                orbitBall=new Navball{Mode="ORBIT"};Add(controls,orbitBall);
                var clock=new StackPanel{Orientation=Orientation.Horizontal};
                var rates=new double[]{1,30,180,600,1800,3600};
                var speed=Options(rates.Select(value=>value.ToString("N0")+"×").ToArray(),Array.IndexOf(rates,warp));speed.Width=72;
                speed.SelectionChanged+=(s,e)=>{if(speed.SelectedIndex>=0)warp=rates[speed.SelectedIndex];};
                clock.Children.Add(speed);
                pauseButton=FlightButton("Pause   Space",TogglePause);clock.Children.Add(pauseButton);
                Add(controls,HudFrame(clock,4));grid.Children.Add(controls);
                var maneuver=new StackPanel();
                Add(maneuver,Mono("MANEUVERS",Cyan),7);
                var steeringModes=new System.Windows.Controls.Primitives.UniformGrid{Columns=2};
                var directionNames=new[]{"Prograde","Retrograde","Radial out","Radial in","Normal","Anti-normal"};
                orbitDirectionButtons=new Button[OrbitAttitudes.Length];
                for(int i=0;i<OrbitAttitudes.Length;i++){
                    int attitude=OrbitAttitudes[i];
                    var direction=FlightButton(directionNames[i],()=>{f.Attitude=attitude;RefreshFlight();});
                    direction.FontSize=11;
                    direction.Margin=new Thickness(0,0,2,2);
                    orbitDirectionButtons[i]=direction;steeringModes.Children.Add(direction);
                }
                Add(maneuver,steeringModes,9);
                flightThrottle=Range(maneuver,"THROTTLE",0,100,f.Throttle*100,1,"%");
                flightThrottle.Focusable=false;
                flightThrottle.ValueChanged+=(s,e)=>{if(!syncing)f.Throttle=flightThrottle.Value/100;};
                engine=FlightButton("Ignite engine   Z",()=>{if(f.Throttle==0)f.Throttle=.25;f.EngineFiring=!f.EngineFiring;RefreshFlight();},true);
                warpNode=FlightButton("Warp to insertion node   N",()=>{RunMissionCoast();RefreshFlight();});
                burn=FlightButton("Assist insertion   B",()=>{RunMissionBurn();RefreshFlight();},true);
                Add(maneuver,engine,6);Add(maneuver,warpNode,6);Add(maneuver,burn,6);
                if(presetAuto)Add(maneuver,FlightButton("Take manual control",()=>{autoOrbitPhase=0;presetAuto=false;RefreshFlight();}),5);
                Add(maneuver,FlightButton("Revert to launch pad",()=>StartAscent(true)));
                orbitTools=PlaceHud(grid,maneuver,HorizontalAlignment.Left,VerticalAlignment.Center,new Thickness(12,12,0,0),230);
                orbitTools.Visibility=showOrbitTools?Visibility.Visible:Visibility.Collapsed;
            }
            last=watch.Elapsed.TotalSeconds;RefreshFlight();
        }
        static readonly int[] OrbitAttitudes={1,-1,2,-2,3,-3};
        void RunMissionCoast(){
            if(transferComputing)return;
            if(!flight.InLunarTransfer){flight.WarpNode();return;}
            flight.WarpLunarArrival();
            if(!flight.Finished){
                paused=true;
                flight.Status="At planned arrival. Check lunar altitude and command capture.";
            }
        }
        bool transferComputing;
        async void RunMissionBurn(){
            if(transferComputing)return;
            if(flight.AwaitingLunarTransfer){
                var active=flight;
                if(!active.PrepareLunarDeparture())return;
                bool resume=!paused;
                paused=true;transferComputing=true;
                active.Status="Calculating lunar departure and capture point…";
                RefreshFlight();
                var epoch=active.System.Copy();var start=active.Position;var target=active.Target;
                try {
                    var solution=await Task.Run(()=>LunarTransfer.Calculate(epoch,start,target));
                    if(flight!=active)return;
                    active.CommitLunarDeparture(solution);
                    if(active.InLunarTransfer&&screen=="flight"){
                        follow=false;orbitMap.FocusBody(3);orbitMap.ZoomToMoonDistance();
                    }
                } catch(ArithmeticException) {
                    if(flight==active)active.Status="Could not calculate that transfer. Recheck the parking orbit.";
                } finally {
                    transferComputing=false;
                    if(flight==active){paused=!resume;RefreshFlight();}
                }
            }else if(flight.InLunarTransfer){
                flight.CaptureMoon();
                if(flight.Reference==Body.Moon){
                    paused=false;warp=1;
                    if(!follow)orbitMap.FocusBody(4);
                }
            }else flight.Insert();
        }
        void ToggleOrbitTools(){
            showOrbitTools=!showOrbitTools;
            orbitTools.Visibility=showOrbitTools?Visibility.Visible:Visibility.Collapsed;
            orbitToolsButton.Content=showOrbitTools?"Hide maneuvers":"Maneuvers";
        }
        void RefreshFlight(){
            if(screen!="flight")return;
            var f=flight;
            orbitMapLayer.Visibility=follow?Visibility.Collapsed:Visibility.Visible;
            scene.Visibility=follow?Visibility.Visible:Visibility.Collapsed;
            if(follow)scene.FlightView(f.Reference==preview.Body?preview:Orbit.Plan(f.Reference,300,300,0,0,0),f,true,vehicle);
            else {
                scene.Backdrop.Visibility=Visibility.Collapsed;
                orbitMap.SetFlight(preview,f==null?null:f.Current,f==null?(Vector3D?)null:f.Position,f==null?null:f.Reference,f==null?null:f.TransferPlan);
            }
            viewHeading.Text=follow?f.Reference.Name+" / Spacecraft":"Navigation";
            foreach(UIElement item in orbitLegend.Children)
                item.Visibility=follow&&item!=orbitLegend.Children[orbitLegend.Children.Count-1]?Visibility.Collapsed:Visibility.Visible;
            if(f!=null)transferLegend.Visibility=!follow&&f.InLunarTransfer?Visibility.Visible:Visibility.Collapsed;
            if(f==null){phaseText.Text="PLANNED ORBIT";orbitSummary.Text=OrbitLabel(mission);return;}
            var orbit=f.Current;
            phaseText.Text="T+ "+TimeSpan.FromSeconds(f.Time).ToString(@"d\.hh\:mm\:ss")+(f.OnPlan?"  ·  ON TARGET":orbit==null?"  ·  OPEN TRAJECTORY":"");
            orbitSummary.Text=string.Format("{0:0.0} km above {6}   ·   {1:N0} m/s orbital\n{5:N0} m/s surface relative   ·   {4:0.0} kg fuel\nPE {2} / AP {3} km",
                f.Position.Length-f.Reference.Radius,f.Velocity.Length*1000,orbit==null?"—":orbit.Pe.ToString("0"),orbit==null?"—":orbit.Ap.ToString("0"),f.Fuel,f.SurfaceVelocity.Length*1000,f.Reference.Name);
            if(f.InLunarTransfer)
                orbitSummary.Text+=string.Format("\nMoon altitude {0:N0} km · capture in {1:0.0} h",f.LunarDistance,Math.Max(0,f.TransferPlan.ArrivalTime-f.Time)/3600);
            if(f.AtmospherePressure>=100)orbitSummary.Text+="\nAerodynamic load "+(f.AtmospherePressure/1000).ToString("0.0")+" kPa";
            status.Text=f.Status;
            orbitViewButton.Content=follow?"Map   M":"Rocket view   M";
            orbitToolsButton.Content=showOrbitTools?"Hide maneuvers":"Maneuvers";
            orbitOperations.Text=string.Format("{0:0} W in / {1:0} W requested\n{2:0} / {3:0} Wh battery\n{4:0.000} / {5:0.0} GB stored\n{6:0.000} / {7:0.000} GB qualifying\n{8} · {9}\n{10}",
                f.GenerationW,f.RequestedW,f.Charge,vehicle[3].A,f.Stored,vehicle[1].A,f.QualifiedReturned,mission.Dose,
                f.Sun(f.Position)?"Sunlight":"Eclipse",f.Contact(f.Position,f.Time)?"Ground in view":"No ground contact",tx?f.RadioState:"Radio standby");
            foreach(int i in f.Vehicle.InstalledInstruments){
                string state=science?f.ExperimentStates[i]:f.ScienceWindow(f.Position,i)?"Observation available":"Outside observation window";
                experimentTelemetry[i].Text=state+string.Format("\n{0:0.000} GB stored · {1:0.000} GB sent",f.StoredByInstrument[i],f.ReturnedByInstrument[i]);
            }
            observeButton.Content=science?"Stop science   E":"Start science   E";
            transmitButton.Content=tx?"Stop downlink   R":"Transmit to Earth   R";
            pauseButton.Content=paused?"Resume   Space":"Pause   Space";
            engine.Content=f.EngineFiring?"Cut engine   X":"Ignite engine   Z";
            engine.IsEnabled=!f.Finished&&f.Fuel>0;
            for(int i=0;i<orbitDirectionButtons.Length;i++){
                var direction=orbitDirectionButtons[i];
                bool selected=OrbitAttitudes[i]==f.Attitude;
                direction.Background=selected?Amber:Slate;direction.Foreground=selected?Dark:White;
                direction.IsEnabled=!f.Finished;
            }
            observeButton.IsEnabled=transmitButton.IsEnabled=pauseButton.IsEnabled=!f.Finished;
            warpNode.Content=f.InLunarTransfer?"Coast to Moon   N":"Warp to insertion node   N";
            burn.Content=f.AwaitingLunarTransfer?"Plan and burn for Moon   B":f.InLunarTransfer?"Lunar capture burn   B":"Assist insertion   B";
            bool maneuverReady=!f.Finished&&!f.EngineFiring&&!transferComputing;
            warpNode.IsEnabled=maneuverReady&&(f.InLunarTransfer?f.Time<f.TransferPlan.ArrivalTime:!f.AwaitingLunarTransfer&&orbit!=null&&!f.Inserted);
            burn.IsEnabled=maneuverReady&&(f.AwaitingLunarTransfer||f.InLunarTransfer||orbit!=null&&!f.Inserted);
            syncing=true;flightThrottle.Value=f.Throttle*100;syncing=false;
            dataBar.Value=Math.Min(1,f.QualifiedReturned/mission.Dose);
            powerBar.Value=vehicle[3].A>0?Math.Min(1,f.Charge/vehicle[3].A):0;
            var radial=f.Position;radial.Normalize();
            var v=f.Velocity;
            if(v.LengthSquared>1e-16)v.Normalize();
            orbitBall.Pitch=Math.Asin(Math.Max(-1,Math.Min(1,Vector3D.DotProduct(f.BurnDirection,radial))))*180/Math.PI;
            orbitBall.VelocityPitch=Math.Asin(Math.Max(-1,Math.Min(1,Vector3D.DotProduct(v,radial))))*180/Math.PI;
            orbitBall.Throttle=f.EngineFiring?f.Throttle:0;orbitBall.InvalidateVisual();
        }
        void ShowReport(){
            var f=flight;
            var report=MissionAnalysis.Report(f,sandbox);
            Clear("report");
            Grid grid;Shell("MISSION REPORT",report.Title,()=>FlightScreen(true),out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var summary=new StackPanel();
            Add(summary,T(report.Score+" / 100",45,White,true),8);
            Add(summary,Mono("SCIENCE "+report.ScienceScore+"/60     ORBIT "+report.OrbitScore+"/20     RESOURCES "+report.ResourceScore+"/20",Cyan),16);
            foreach(string finding in report.Findings)Add(summary,T(finding,14,Muted),12);
            Add(summary,FlightButton("Revise vehicle",Build,true));
            Column(grid,Frame(Scroll(summary),17),0);
            var events=new StackPanel();Add(events,T("Flight log",22,White,true),12);
            Add(events,new OperationsPlot(f){Height=154},8);
            var plotLegend=new StackPanel{Orientation=Orientation.Horizontal};
            Add(plotLegend,Mono("━━  BATTERY CHARGE     ",Cyan,10));
            Add(plotLegend,Mono("━━  RECORDER USED",Amber,10));
            Add(events,plotLegend,13);
            Add(events,T("Delivered science",18,White,true),7);
            foreach(int i in f.Vehicle.InstalledInstruments)Add(events,T(string.Format("{0} · {1:0.000} GB delivered",Vehicle.Catalog[5][i].Name,f.ReturnedByInstrument[i]),13),7);
            Add(events,T("Events",18,White,true),9);
            foreach(var entry in f.Events)Add(events,Mono(entry.ToString(),White,12),9);
            Column(grid,Frame(Scroll(events),17),1);
        }
        void ShowAscentReport(){
            var a=launch;
            Clear("report");
            Grid grid;Shell("MISSION REPORT",a.Failed?"Launch ended":"Launch progress",()=>StartAscent(false),out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            var details=new StackPanel();
            Add(details,T(a.Status,21,White,true),12);
            Add(details,T(string.Format("Time {0:0.0} s · peak altitude {1:0.0} km\nLaunch mass {2:N0} kg · {3:0.00} pad thrust-to-weight\nNo spacecraft has been released. Science delivered 0 GB.",
                a.Time,a.Track.Max(sample=>sample.Altitude),a.Design.LaunchMass(a.Payload),a.Design.PadTwr(a.Payload,a.Mission.Body)),14,Muted),14);
            foreach(string issue in vehicle.Issues(mission))Add(details,T(issue,13,Amber),7);
            if(a.Failed&&a.DynamicPressure>155000)Add(details,T("Use less throttle while the air is dense. More thrust can increase aerodynamic loads before reaching space.",13,Muted),12);
            Add(details,FlightButton("Revise vehicle",hellasMission?(Action)MarsWorkshop:Build,true),7);
            Add(details,FlightButton("Revert to launch pad",()=>StartAscent(true)));
            Column(grid,Frame(Scroll(details),18),0);
            var timeline=new StackPanel();
            Add(timeline,T("Altitude by flight time",22,White,true),12);
            Add(timeline,new AscentChart(a){Height=260});
            Column(grid,Frame(timeline,18),1);
        }
    }
    sealed class OperationsPlot : FrameworkElement {
        static readonly Brush Background=BrushOf(14,21,26);
        static readonly Brush GridLine=BrushOf(55,67,73);
        static readonly Brush PowerLine=BrushOf(129,204,181);
        static readonly Brush RecorderLine=BrushOf(223,192,120);
        readonly Flight flight;
        public OperationsPlot(Flight flight){this.flight=flight;}
        static Brush BrushOf(byte r,byte g,byte b){
            var brush=new SolidColorBrush(Color.FromRgb(r,g,b));brush.Freeze();return brush;
        }
        protected override void OnRender(DrawingContext context){
            base.OnRender(context);
            if(ActualWidth<30||ActualHeight<30)return;
            context.DrawRectangle(Background,null,new Rect(0,0,ActualWidth,ActualHeight));
            const double inset=10;
            double height=ActualHeight-inset*2,width=ActualWidth-inset*2;
            for(int i=0;i<=4;i++){
                double y=inset+height*i/4;
                context.DrawLine(new Pen(GridLine,1),new Point(inset,y),new Point(ActualWidth-inset,y));
            }
            if(flight.History.Count<2)return;
            double first=flight.History[0].Time,span=Math.Max(1,flight.History.Last().Time-first);
            double battery=Math.Max(.001,flight.Vehicle[3].A),recorder=Math.Max(.001,flight.Vehicle[1].A);
            DrawSeries(context,PowerLine,sample=>sample.Charge/battery,first,span,width,height,inset);
            DrawSeries(context,RecorderLine,sample=>sample.Stored/recorder,first,span,width,height,inset);
        }
        void DrawSeries(DrawingContext context,Brush brush,Func<FlightSample,double> value,double start,double span,double width,double height,double inset){
            var geometry=new StreamGeometry();
            using(var line=geometry.Open()){
                for(int i=0;i<flight.History.Count;i++){
                    var sample=flight.History[i];
                    var point=new Point(inset+(sample.Time-start)/span*width,inset+(1-Math.Min(1,Math.Max(0,value(sample))))*height);
                    if(i==0)line.BeginFigure(point,false,false);else line.LineTo(point,true,false);
                }
            }
            context.DrawGeometry(null,new Pen(brush,1.8),geometry);
        }
    }
}

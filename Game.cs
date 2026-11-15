using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Perilune {
    sealed partial class Game : Window {
        static readonly SolidColorBrush Dark=B(0x141617), Panel=B(0x1c2020), Slate=B(0x282e2d), Line=B(0x434c49),
            White=B(0xe8eff5), Muted=B(0xa5b6c6), Amber=B(0xdacb9b), Cyan=B(0x94c5ba), Red=B(0xffab93);
        readonly Grid baseGrid=new Grid();
        readonly DispatcherTimer timer=new DispatcherTimer();
        readonly Stopwatch watch=Stopwatch.StartNew();
        string screen="";
        Mission mission;
        Vehicle vehicle;
        Flight flight;
        Scene scene;
        bool sandbox, stack=true, follow, paused, science, tx;
        bool presetAuto;
        int autoOrbitPhase;
        double autoOrbitNext;
        double warp=180, last;
        int slot;
        TextBlock status, phaseText, viewHeading, guideLegend, flownLegend;
        ProgressBar dataBar, powerBar;
        Button warpNode, burn, pauseButton, observeButton, transmitButton;
        Orbit preview;
        Button engine;
        Slider flightThrottle;
        static SolidColorBrush B(int hex) {
            return new SolidColorBrush(Color.FromRgb((byte)(hex>>16),(byte)(hex>>8),(byte)hex));
        }
        public Game() {
            Title="PERILUNE  /  Flight Program";
            Width=1360;Height=850;MinWidth=1000;MinHeight=660;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            Background=Dark;Foreground=White;FontFamily=new FontFamily("Segoe UI");FontSize=14;
            Content=baseGrid;
            timer.Interval=TimeSpan.FromMilliseconds(40);
            timer.Tick+=(s,e)=>Update();
            timer.Start();
            KeyDown+=(s,e)=>{
                if(screen=="build"||screen=="mars-rocket"){BuildKeys(e);return;}
                if(screen=="mars-build"&&e.Key==Key.Z&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){UndoRoverEdit();e.Handled=true;return;}
                if(screen=="mars-flight"){MarsKeys(e);return;}
                if(screen=="launch"){LaunchKeys(e);return;}
                if(screen!="flight"||flight==null||e.OriginalSource is ComboBox||e.OriginalSource is ComboBoxItem)return;
                switch(e.Key){
                    case Key.Space: TogglePause();e.Handled=true;break;
                    case Key.M:case Key.F9: if(!e.IsRepeat)Follow(!follow);break;
                    case Key.Z: flight.Throttle=1;flight.EngineFiring=true;RefreshFlight();break;
                    case Key.X: flight.EngineFiring=false;flight.Throttle=0;RefreshFlight();break;
                    case Key.Up:case Key.Down:
                        double increment=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?.01:.05;
                        flight.Throttle=Math.Max(0,Math.Min(1,flight.Throttle+(e.Key==Key.Up?increment:-increment)));
                        e.Handled=true;RefreshFlight();break;
                    case Key.N: if(!e.IsRepeat&&!flight.EngineFiring)RunMissionCoast();RefreshFlight();break;
                    case Key.B: if(!flight.EngineFiring)RunMissionBurn();RefreshFlight();break;
                    case Key.E: if(!e.IsRepeat&&!flight.Finished)science=!science;RefreshFlight();break;
                    case Key.R: if(!e.IsRepeat&&!flight.Finished)tx=!tx;RefreshFlight();break;
                }
            };
            Menu();
        }
        static TextBlock T(string text,double size=14,Brush brush=null,bool bold=false) {
            return new TextBlock { Text=text, FontSize=size, Foreground=brush??White,
                FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap };
        }
        static TextBlock Mono(string text,Brush color=null,double size=11) {
            var t=T(text,size,color??Muted,true);t.FontFamily=new FontFamily("Consolas");return t;
        }
        static void Add(Panel panel,UIElement e,double bottom=0) {
            var f=e as FrameworkElement;if(f!=null)f.Margin=new Thickness(f.Margin.Left,f.Margin.Top,f.Margin.Right,bottom);
            panel.Children.Add(e);
        }
        static Button Btn(string label,Action action,bool main=false) {
            var b=new Button {Content=label,Padding=new Thickness(14,8,14,8),MinHeight=35,Cursor=Cursors.Hand,
                HorizontalContentAlignment=HorizontalAlignment.Left, Background=main?Amber:Slate,
                Foreground=main?Dark:White,BorderBrush=main?Amber:Line,FontWeight=FontWeights.SemiBold};
            var border=new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));
            border.SetValue(Border.BorderThicknessProperty,new Thickness(1));
            border.SetBinding(Border.BorderBrushProperty,new System.Windows.Data.Binding("BorderBrush"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            var content=new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
            content.SetValue(FrameworkElement.MarginProperty,new Thickness(12,7,12,7));
            content.SetBinding(FrameworkElement.HorizontalAlignmentProperty,new System.Windows.Data.Binding("HorizontalContentAlignment"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
            border.AppendChild(content);
            b.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
            b.MouseEnter+=(s,e)=>b.BorderBrush=Cyan;
            b.MouseLeave+=(s,e)=>b.BorderBrush=main?Amber:Line;
            b.IsEnabledChanged+=(s,e)=>b.Opacity=b.IsEnabled?1:.45;
            b.Click+=(s,e)=>action();
            return b;
        }
        static Border Frame(UIElement child,double pad=16) {
            return new Border {Child=child,Background=Panel,BorderBrush=Line,BorderThickness=new Thickness(1),
                CornerRadius=new CornerRadius(2),Padding=new Thickness(pad),ClipToBounds=true};
        }
        static ScrollViewer Scroll(UIElement child) {
            return new ScrollViewer{Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        }
        void Clear(string name) { screen=name;baseGrid.Children.Clear(); }
        static void Column(Grid g,UIElement e,int i) { Grid.SetColumn(e,i);g.Children.Add(e); }
        static Border Sky() {
            var canvas=new Canvas{IsHitTestVisible=false,ClipToBounds=true};
            var sky=new Border{Child=canvas,Background=new RadialGradientBrush(B(0x192c41).Color,Dark.Color){Center=new Point(.70,.55),GradientOrigin=new Point(.70,.55),RadiusX=.67,RadiusY=.79}};
            sky.SizeChanged+=(s,e)=>{
                canvas.Children.Clear();
                var rng=new Random(309);
                for(int i=0;i<310;i++){
                    var dot=new System.Windows.Shapes.Ellipse {Width=i%19==0?2.2:1.25,Height=i%19==0?2.2:1.25,Fill=White,Opacity=.15+rng.NextDouble()*.5};
                    Canvas.SetLeft(dot,rng.NextDouble()*sky.ActualWidth);Canvas.SetTop(dot,rng.NextDouble()*sky.ActualHeight);canvas.Children.Add(dot);
                }
            };
            return sky;
        }
        void Menu() {
            Clear("menu");flight=null;
            baseGrid.Children.Add(Sky());
            var display=new Grid{Margin=new Thickness(46,0,0,0)};
            display.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(445)});
            display.ColumnDefinitions.Add(new ColumnDefinition());
            var hero=new Scene(()=>{});
            hero.IsHitTestVisible=false;
            hero.Yaw=42;hero.Pitch=12;hero.Zoom=.87;
            var marsBackdrop=new Body("Mars","mars.jpg",MarsCruise.MarsRadius,SolarSystem.GM[5],MarsCruise.MarsRotation);
            hero.FlightView(Orbit.Plan(marsBackdrop,310,1650,53,8,16),null,false,new Vehicle());
            Column(display,hero,1);
            var menu=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(33,0,12,0)};
            Add(menu,T("PERILUNE",78,White,true),27);
            var sb=Btn("Sandbox",SandboxMenu,true);sb.FontSize=17;sb.Height=54;
            var missions=Btn("Choose Mission",()=>Archive(-1));missions.FontSize=17;missions.Height=54;
            Add(menu,sb,9);Add(menu,missions);
            Column(display,menu,0);baseGrid.Children.Add(display);
        }
        StackPanel Shell(string section,string line,Action back,out Grid area) {
            var layout=new DockPanel{Margin=new Thickness(24,16,24,19),LastChildFill=true};
            baseGrid.Children.Add(layout);
            var header=new DockPanel{Margin=new Thickness(0,0,0,14)};
            DockPanel.SetDock(header,Dock.Top);layout.Children.Add(header);
            var go=Btn("‹  Back",back);go.VerticalAlignment=VerticalAlignment.Center;go.Margin=new Thickness(0,0,14,0);
            DockPanel.SetDock(go,Dock.Right);header.Children.Add(go);
            var labels=new StackPanel();header.Children.Add(labels);
            Add(labels,Mono("PERILUNE    /    "+section,Cyan,12),3);
            Add(labels,T(line,30,White,true));
            area=new Grid();layout.Children.Add(area);return labels;
        }
        void Archive(int index) {
            Clear("archive");
            Grid grid;Shell("MISSION ARCHIVE","Choose a mission",Menu,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(296)});
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var list=new StackPanel{Margin=new Thickness(0,0,9,0)};
            Column(grid,Frame(Scroll(list)),0);
            Add(list,Btn("01   Hellas Survey",()=>Archive(-1),index==-1),5);
            for(int i=0;i<Mission.Archive.Length;i++) {
                int j=i;var m=Mission.Archive[i];
                Add(list,Btn((i+2).ToString("00")+"   "+m.Name,()=>Archive(j),i==index),5);
            }
            if(index==-1){HellasBrief(grid);return;}
            var current=Mission.Archive[index];
            var split=new Grid();split.ColumnDefinitions.Add(new ColumnDefinition());split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(305)});
            Column(grid,split,1);
            Scene camera=null;var path=current.Target;
            camera=new Scene(()=>camera.FlightView(path,null,false,new Vehicle()));
            camera.Reset(false);camera.FlightView(path,null,false,new Vehicle());
            var display=new Grid();display.Children.Add(Sky());display.Children.Add(camera);
            Column(split,Frame(display,0),0);
            var brief=new StackPanel();
            Add(brief,Mono("FLIGHT PLAN "+(index+2).ToString("00")+" / "+(Mission.Archive.Length+1),Cyan),10);
            Add(brief,T(current.Name,27,White,true),10);
            Add(brief,T(current.Objective,15,Muted),19);
            Brief(brief,current);
            var mode=Options(new[]{"Auto orbital trajectory", "Manual orbital trajectory"},0);
            Add(brief,mode,8);
            Add(brief,Btn("Accept mission  ›",()=>SelectMission(current,false,mode.SelectedIndex==0),true));
            Column(split,Frame(Scroll(brief)),1);
        }
        static string OrbitLabel(Mission m) {
            return string.Format("{0} × {1} km   /   {2:0.#}°",m.Pe,m.Ap,m.Inclination);
        }
        static void Brief(StackPanel brief,Mission m) {
            Add(brief,Mono("ASSIGNED TRAJECTORY",Cyan),5);
            if(m.Body.Lunar)Add(brief,T("Earth launch → lunar transfer → capture",12,Amber,true),5);
            Add(brief,T(m.Body.Name+"  /  "+OrbitLabel(m),14,White,true),5);
            Add(brief,T(string.Format("Ascending node {0:0.#}° · Periapsis argument {1:0.#}°",m.Node,m.Arg),12,Muted),15);
            Add(brief,Mono("REQUIRED EXPERIMENT",Cyan),5);
            Add(brief,T(Vehicle.Catalog[5][m.Instrument].Name,14,Amber,true),4);
            Add(brief,T(m.Experiment,13,Muted),15);
            Add(brief,Mono("RETURN REQUIREMENTS",Cyan),5);
            Add(brief,T(string.Format("{0:0.00} GB to ground · {1:0} h {3}\n{2:0} M mission budget",m.Dose,m.Hours,m.Budget,m.Body.Lunar?"after lunar capture":"lifetime"),13),17);
        }
        static ComboBox Options(string[] entries,int index) {
            var c=new ComboBox{ItemsSource=entries,SelectedIndex=index,Foreground=Dark,FontSize=14,MinHeight=30};
            return c;
        }
        static Slider Range(StackPanel layout,string label,double min,double max,double value,double step,string units) {
            var title=Mono(label,Cyan);Add(layout,title,2);
            var slider=new Slider{Minimum=min,Maximum=max,Value=value,TickFrequency=step,IsSnapToTickEnabled=true};
            Action update=()=>title.Text=label+"    "+slider.Value.ToString("0.##")+units;
            slider.ValueChanged+=(s,e)=>update();update();Add(layout,slider,12);return slider;
        }
        void SandboxSetup(){SandboxSetup(false);}
        void SandboxSetup(bool revise) {
            Clear("setup");
            Grid grid;Shell("SANDBOX","Design a custom flight",revise?(Action)Build:SandboxMenu,out grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(365)});grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left=new StackPanel();
            Add(left,Mono("OBJECTIVE",Cyan),4);
            var title=new TextBox{Text=revise?mission.Objective:"Orbital survey",MaxLength=65,FontSize=15,Padding=new Thickness(6)};
            Add(left,title,11);
            Add(left,Mono("PRIMARY BODY",Cyan),4);
            var body=Options(new[]{"Earth","Moon"},revise&&mission.Body.Lunar?1:0);Add(left,body,11);
            Add(left,Mono("EXPERIMENT",Cyan),4);
            var instrument=Options(Vehicle.Catalog[5].Select(p=>p.Name).ToArray(),revise?mission.Instrument:0);Add(left,instrument,10);
            var hours=Range(left,"LIFETIME",6,96,revise?mission.Hours:24,1," h");
            var data=Range(left,"RETURN TARGET",.1,3,revise?mission.Dose:.4,.05," GB");
            var budget=Range(left,"BUDGET",40,160,revise?mission.Budget:100,1," M");
            Column(grid,Frame(Scroll(left)),0);
            var right=new Grid();right.RowDefinitions.Add(new RowDefinition());right.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var sky=new Grid();sky.Children.Add(Sky());right.Children.Add(sky);
            Scene camera=null;
            Orbit estimate=null;
            camera=new Scene(()=>{if(estimate!=null)camera.FlightView(estimate,null,false,new Vehicle());});camera.Reset(false);sky.Children.Add(camera);
            var fields=new StackPanel{Margin=new Thickness(12)};
            var pairs=new UniformGrid{Columns=2,Rows=3};
            Add(fields,pairs,3);
            var boxes=Enumerable.Range(0,6).Select(i=>new StackPanel{Margin=new Thickness(8,0,8,0)}).ToArray();
            foreach(var b in boxes)pairs.Children.Add(b);
            var lunarStart=body.SelectedIndex==1;
            var pe=Range(boxes[0],"PERIAPSIS",lunarStart?50:200,2000,revise?mission.Pe:350,10," km");
            var ap=Range(boxes[1],"APOAPSIS",lunarStart?50:200,lunarStart?6500:16000,revise?mission.Ap:1300,10," km");
            var inc=Range(boxes[2],"INCLINATION",0,180,revise?mission.Inclination:39,1,"°");
            var node=Range(boxes[3],"ASCENDING NODE",0,359,revise?mission.Node:18,1,"°");
            var arg=Range(boxes[4],"PERIAPSIS ARGUMENT",0,359,revise?mission.Arg:32,1,"°");
            Action update=()=>{
                estimate=Orbit.Plan(body.SelectedIndex==0?Body.Earth:Body.Moon,pe.Value,Math.Max(pe.Value,ap.Value),inc.Value,node.Value,arg.Value);
                camera.FlightView(estimate,null,false,new Vehicle());
            };
            pe.ValueChanged+=(s,e)=>{if(pe.Value>ap.Value)ap.Value=pe.Value;update();};
            ap.ValueChanged+=(s,e)=>{if(ap.Value<pe.Value)pe.Value=ap.Value;update();};
            foreach(var slider in new[]{inc,node,arg})slider.ValueChanged+=(s,e)=>update();
            body.SelectionChanged+=(s,e)=>{
                bool lunar=body.SelectedIndex==1;
                pe.Minimum=ap.Minimum=lunar?50:200;ap.Maximum=lunar?6500:16000;
                pe.Value=lunar?110:350;ap.Value=lunar?640:1300;update();
            };
            Add(boxes[5],Btn(revise?"Apply flight plan  ›":"Build vehicle  ›",()=>{
                string objective=string.IsNullOrWhiteSpace(title.Text)?"Orbital experiment":title.Text.Trim();
                string exp=instrument.SelectedIndex<2?"Run the selected instrument in daylight.":"Run the selected instrument in orbit.";
                var plan=new Mission("Sandbox",estimate.Body,pe.Value,ap.Value,inc.Value,node.Value,arg.Value,hours.Value,data.Value,
                    budget.Value,instrument.SelectedIndex,objective,exp);
                if(revise){mission=plan;comparison=null;Build();}
                else SelectMission(plan,true);
            },true));
            var controls=Frame(fields,0);Grid.SetRow(controls,1);right.Children.Add(controls);
            right.Margin=new Thickness(10,0,0,0);Column(grid,right,1);update();
        }
        void SelectMission(Mission m,bool free,bool guided=false) {
            hellasMission=false;
            presetAuto=guided&&!free;
            mission=m;sandbox=free;vehicle=new Vehicle();
            vehicle.Stack=StarterStack();
            vehicle.Selections[1]=0;
            vehicle.Selections[5]=m.Instrument;
            vehicle.Selections[3]=m.Instrument==2?2:1;
            if(m.Body.Lunar)vehicle.Selections[2]=3;
            mapFocus=3;
            buildUndo.Clear();selectedPart="";comparison=null;designName="Untitled vehicle";lastEditorCamera=null;
            stack=true;rocketCategory=0;slot=1;Build();
        }
        static ProgressBar Bar() {
            return new ProgressBar{Height=6,Minimum=0,Maximum=1,Foreground=Cyan,Background=Line};
        }
        void Follow(bool target) { follow=target;scene.Reset(false);RefreshFlight(); }
        void TogglePause() { paused=!paused;RefreshFlight(); }
        void Update() {
            double now=watch.Elapsed.TotalSeconds,dt=Math.Min(.24,now-last);last=now;
            if(screen=="launch"){UpdateLaunch(dt);return;}
            if(screen=="mars-flight"){UpdateMars(dt);return;}
            if(screen=="mars-cruise"){UpdateMarsCruise(dt);return;}
            if(screen=="flight"&&flight!=null&&presetAuto&&autoOrbitPhase>0&&now>=autoOrbitNext)
                AdvancePresetGuidance(now);
            if(screen!="flight"||flight==null||paused||transferComputing||flight.Finished)return;
            double step=dt*(flight.EngineFiring?1:warp);
            if(flight.InLunarTransfer&&warp>1&&!flight.EngineFiring){
                double remaining=flight.TransferPlan.ArrivalTime-flight.Time;
                if(remaining>0&&remaining<step){
                    step=remaining;paused=true;
                    flight.Status="At planned arrival. Check lunar altitude and command capture.";
                }
            }
            flight.Tick(step,(science?1:0)|(tx?2:0));RefreshFlight();
        }
        void AdvancePresetGuidance(double now){
            if(flight.Finished||flight.EngineFiring){autoOrbitPhase=0;return;}
            if(transferComputing)return;
            if(paused&&autoOrbitPhase!=3)return;
            autoOrbitNext=now+1.5;
            switch(autoOrbitPhase){
                case 1:
                    if(flight.AwaitingLunarTransfer){autoOrbitPhase=2;RunMissionBurn();}
                    else {autoOrbitPhase=4;RunMissionCoast();}
                    break;
                case 2:
                    if(flight.InLunarTransfer){autoOrbitPhase=3;RunMissionCoast();}
                    else autoOrbitPhase=0;
                    break;
                case 3:
                    if(flight.InLunarTransfer){RunMissionBurn();autoOrbitPhase=flight.Reference==Body.Moon?1:0;}
                    else autoOrbitPhase=0;
                    break;
                case 4:
                    autoOrbitPhase=0;RunMissionBurn();
                    break;
            }
            RefreshFlight();
        }
        [STAThread]
        public static void Main(string[] args) {
            if(args.Contains("--verify")) { Environment.ExitCode=Verification.Run()?0:1;return; }
            var app=new Application();app.Run(new Game());
        }
    }
}

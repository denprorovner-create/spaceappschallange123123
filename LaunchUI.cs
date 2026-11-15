using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Game {
        Ascent launch;
        double launchRate=1;
        TextBlock launchReadout, launchMessage, launchAltitude, launchVelocity, launchVelocityDetail, launchClock, launchOrbit, launchStage, launchFuelText;
        Scene launchScene;
        LinearGradientBrush launchSky;
        AscentChart ascentChart;
        CheckBox guidance, autoStage;
        Slider throttle, steering;
        Button ignite, stageButton, fairingButton, launchMapButton, launchToolsButton, padCameraButton;
        ComboBox launchSpeed;
        bool syncing, ascentMap, launchToolsOpen, stageCloseups=true, returnToMapAfterStage;
        int seenSeparation;
        double closeupRemaining;
        SystemMap ascentMapScene;
        Grid rocketViewport, mapViewport;
        Orbit launchTarget;
        Border launchTools;
        Navball launchBall;
        ProgressBar coreGauge, upperGauge, boosterGauge;
        CheckBox Check(string name,bool value,Action<bool> changed){
            var c=new CheckBox{Content=name,Foreground=White,IsChecked=value,Focusable=false,Padding=new Thickness(3),Margin=new Thickness(0,0,0,9)};
            c.Checked+=(s,e)=>changed(true);c.Unchecked+=(s,e)=>changed(false);return c;
        }
        static Button FlightButton(string label,Action action,bool main=false){
            var b=Btn(label,action,main);b.Focusable=false;return b;
        }
        void LaunchCommand(){
            if(!launch.Started){
                if(launchRate==0){launchRate=1;launchSpeed.SelectedIndex=1;}
                launch.Start();
            }else if(launch.Countdown<=0){
                launch.Auto=false;guidance.IsChecked=false;launch.Throttle=launch.Throttle>0?0:1;
            }
            RefreshLaunch();
        }
        void ToggleLaunchTools(){
            launchToolsOpen=!launchToolsOpen;
            launchTools.Visibility=launchToolsOpen?Visibility.Visible:Visibility.Collapsed;
            launchToolsButton.Content=launchToolsOpen?"Close instruments":"Flight tools";
        }
        void StartAscent(bool reset) {
            if(reset){
                var epoch=hellasMission?MarsCruise.LaunchEpoch(MarsCruise.DefaultDepartureDays+launchDay):new SolarSystem();
                if(hellasMission){
                    launchBaseTime=epoch.Time;
                    mission=MarsCruise.EarthParking(epoch,autoTrajectory?MarsCruise.DefaultFlightDays:flightDays);
                    mission.Pe=mission.Ap=initialParkingAltitude;
                    vehicle=new Vehicle{Stack=hellasStack.Copy()};
                }
                var departure=LunarTransfer.EarthLaunch(mission,epoch);
                launch=new Ascent(departure,vehicle.Stack,hellasMission?HellasMass:vehicle.Wet,epoch);
                bool guided=hellasMission?autoTrajectory:presetAuto;
                launchRate=guided?20:1;flight=null;launch.Auto=guided;launch.AutoStage=guided;autoOrbitPhase=0;
                ascentMap=false;launchTarget=departure.Target;launchToolsOpen=false;
                closeupRemaining=0;seenSeparation=0;returnToMapAfterStage=false;
            }
            Clear("launch");
            var g=new Grid{ClipToBounds=true};baseGrid.Children.Add(g);
            rocketViewport=new Grid();rocketViewport.Children.Add(Sky());g.Children.Add(rocketViewport);
            if(!launch.Mission.Body.Lunar){
                launchSky=new LinearGradientBrush(B(0x256999).Color,B(0xb5dce8).Color,90);
                rocketViewport.Children.Add(new Border{Background=launchSky,IsHitTestVisible=false});
            }else launchSky=null;
            launchScene=new Scene(RefreshLaunch);launchScene.Reset(true);
            if(hellasMission){launchScene.MarsPayload=roverBuild;launchScene.MarsCruisePackage=cruisePackage;}
            rocketViewport.Children.Add(launchScene.Backdrop);rocketViewport.Children.Add(launchScene);
            mapViewport=new Grid();
            ascentMapScene=AddNavigationMap(mapViewport,launch.System);
            g.Children.Add(mapViewport);
            var top=new DockPanel{LastChildFill=true};
            var back=FlightButton("‹  Assembly",hellasMission?(Action)OpenHellasRocket:Build);back.Margin=new Thickness(0,0,14,0);DockPanel.SetDock(back,Dock.Left);top.Children.Add(back);
            var switches=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(switches,Dock.Right);top.Children.Add(switches);
            launchMapButton=FlightButton("Map   M",()=>ShowAscentMap(!ascentMap));launchMapButton.Margin=new Thickness(0,0,5,0);switches.Children.Add(launchMapButton);
            padCameraButton=FlightButton("Pad camera",()=>{
                launchScene.PadCamera=!launchScene.PadCamera;ascentMap=false;returnToMapAfterStage=false;RefreshLaunch();
            });
            switches.Children.Add(padCameraButton);
            launchToolsButton=FlightButton("Flight tools",ToggleLaunchTools);launchToolsButton.Margin=new Thickness(0,0,5,0);switches.Children.Add(launchToolsButton);
            switches.Children.Add(FlightButton("Report",ShowAscentReport));
            var title=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            Add(title,Mono("PERILUNE  /  FLIGHT",Cyan,10),2);Add(title,T(mission.Name,16,White,true));top.Children.Add(title);
            var header=HudFrame(top,8);header.VerticalAlignment=VerticalAlignment.Top;header.Margin=new Thickness(10,9,10,0);g.Children.Add(header);
            var metrics=new Grid();metrics.ColumnDefinitions.Add(new ColumnDefinition());metrics.ColumnDefinitions.Add(new ColumnDefinition());
            var alt=new StackPanel();Add(alt,Mono("ALTITUDE  ·  ASL",Muted,10),2);launchAltitude=Mono("",White,25);Add(alt,launchAltitude);
            var assistSwitch=FlightButton("Guided ascent",ToggleGuidedAscent);assistSwitch.ToolTip="Toggle steering and staging assists together. W/S, A/D, and throttle keys take back manual steering.";
            switches.Children.Insert(switches.Children.Count-1,assistSwitch);
            launchGuided=assistSwitch;
            var velocity=new StackPanel{Margin=new Thickness(12,0,0,0)};
            Add(velocity,Mono("SURFACE SPEED",Muted,10),2);
            launchVelocity=Mono("",White,25);Add(velocity,launchVelocity,2);
            launchVelocityDetail=Mono("",Muted,10);Add(velocity,launchVelocityDetail);
            Column(metrics,alt,0);Column(metrics,velocity,1);
            var metricFrame=HudFrame(metrics,11);metricFrame.Width=422;metricFrame.HorizontalAlignment=HorizontalAlignment.Center;metricFrame.VerticalAlignment=VerticalAlignment.Top;metricFrame.Margin=new Thickness(0,78,0,0);metricFrame.IsHitTestVisible=false;g.Children.Add(metricFrame);
            launchMessage=Mono("",White,13);launchMessage.TextAlignment=TextAlignment.Center;launchMessage.MaxWidth=510;
            var messageFrame=HudFrame(launchMessage,8);messageFrame.HorizontalAlignment=HorizontalAlignment.Center;messageFrame.VerticalAlignment=VerticalAlignment.Top;messageFrame.Margin=new Thickness(0,170,0,0);messageFrame.IsHitTestVisible=false;g.Children.Add(messageFrame);
            var stages=new StackPanel();
            launchClock=Mono("",Cyan,12);Add(stages,launchClock,8);
            launchStage=Mono("",White,11);Add(stages,launchStage,6);
            ignite=FlightButton("LAUNCH   [Enter]",LaunchCommand,true);ignite.MinHeight=44;Add(stages,ignite,6);
            stageButton=FlightButton("Stage   [Space]",()=>{launch.Separate();RefreshLaunch();});stageButton.MinHeight=40;Add(stages,stageButton,9);
            fairingButton=FlightButton("Jettison fairing   [F]",()=>{launch.JettisonFairing();RefreshLaunch();});
            Add(stages,fairingButton,8);
            var speedRow=new DockPanel();var warpLabel=Mono("TIME WARP",Muted,10);warpLabel.VerticalAlignment=VerticalAlignment.Center;speedRow.Children.Add(warpLabel);
            var rates=new[]{0d,1,4,8,20,45};
            launchSpeed=Options(new[]{"Paused","1×","4×","8×","20×","45×"},Array.IndexOf(rates,launchRate));launchSpeed.Focusable=false;launchSpeed.Width=94;launchSpeed.HorizontalAlignment=HorizontalAlignment.Right;
            launchSpeed.SelectionChanged+=(s,e)=>{if(launchSpeed.SelectedIndex>=0)launchRate=rates[launchSpeed.SelectedIndex];Focus();};speedRow.Children.Add(launchSpeed);Add(stages,speedRow);
            var stageFrame=HudFrame(stages);stageFrame.Width=226;stageFrame.HorizontalAlignment=HorizontalAlignment.Left;stageFrame.VerticalAlignment=VerticalAlignment.Bottom;stageFrame.Margin=new Thickness(14,0,0,15);g.Children.Add(stageFrame);
            var instruments=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,9)};
            launchBall=new Navball();Add(instruments,launchBall);
            var keys=Mono("W/S pitch  ·  A/D heading  ·  Z/X throttle  ·  F fairing  ·  M map",White,10);keys.TextAlignment=TextAlignment.Center;
            Add(instruments,HudFrame(keys,5));g.Children.Add(instruments);
            var resources=new StackPanel();
            Add(resources,Mono("PROPELLANT",Cyan,10),7);
            Add(resources,Mono("CORE",Muted,10),3);coreGauge=Bar();coreGauge.Height=5;Add(resources,coreGauge,7);
            Add(resources,Mono("BOOSTERS",Muted,10),3);boosterGauge=Bar();boosterGauge.Height=5;Add(resources,boosterGauge,7);
            Add(resources,Mono("UPPER",Muted,10),3);upperGauge=Bar();upperGauge.Height=5;Add(resources,upperGauge,8);
            launchFuelText=Mono("",White,11);Add(resources,launchFuelText,8);
            if(hellasMission)Add(resources,Mono(string.Format("CRUISE    {0:N0} kg reserved\nLANDING     {1:N0} kg reserved",
                stageFuel[cruisePackage],roverBuild.LandingFuel),Cyan,10),7);
            launchOrbit=Mono("",Amber,11);launchOrbit.LineHeight=17;Add(resources,launchOrbit);
            var resourceFrame=HudFrame(resources);resourceFrame.Width=226;resourceFrame.HorizontalAlignment=HorizontalAlignment.Right;resourceFrame.VerticalAlignment=VerticalAlignment.Bottom;resourceFrame.Margin=new Thickness(0,0,14,15);g.Children.Add(resourceFrame);
            var flightTools=new StackPanel();
            Add(flightTools,Mono("FLIGHT TOOLS",Cyan,11),10);
            guidance=Check("Guidance assist",launch.Auto,value=>{launch.Auto=value;RefreshLaunch();});Add(flightTools,guidance);
            autoStage=Check("Automatic separation",launch.AutoStage,value=>{launch.AutoStage=value;});Add(flightTools,autoStage);
            Add(flightTools,Check("Staging close-ups",stageCloseups,value=>{
                stageCloseups=value;
                if(!value)EndStagingCloseup();
            }));
            throttle=Range(flightTools,"LIQUID THROTTLE",0,100,launch.Throttle*100,1,"%");throttle.Focusable=false;
            throttle.ValueChanged+=(s,e)=>{if(!syncing){launch.Auto=false;guidance.IsChecked=false;launch.Throttle=throttle.Value/100;RefreshLaunch();}};
            steering=Range(flightTools,"PITCH",-45,105,launch.Pitch,1,"°");steering.Focusable=false;
            steering.ValueChanged+=(s,e)=>{if(!syncing){launch.Auto=false;guidance.IsChecked=false;launch.Pitch=steering.Value;RefreshLaunch();}};
            Add(flightTools,FlightButton("Release spacecraft now",()=>{launch.ReleaseNow();RefreshLaunch();}),9);
            launchReadout=Mono("",White,11);launchReadout.LineHeight=18;Add(flightTools,launchReadout,8);
            ascentChart=new AscentChart(launch){Height=92};Add(flightTools,ascentChart,8);
            Add(flightTools,FlightButton("Revert to pad",()=>StartAscent(true)),6);
            Add(flightTools,T("Hold Shift for precision steering and throttle. P pauses · Space stages. Solids burn until exhausted or jettisoned.",11,Muted));
            launchTools=HudFrame(Scroll(flightTools),12);launchTools.Width=246;launchTools.HorizontalAlignment=HorizontalAlignment.Right;launchTools.Margin=new Thickness(0,80,14,248);launchTools.Visibility=launchToolsOpen?Visibility.Visible:Visibility.Collapsed;g.Children.Add(launchTools);
            launchToolsButton.Content=launchToolsOpen?"Close instruments":"Flight tools";
            last=watch.Elapsed.TotalSeconds;RefreshLaunch();Focus();
        }
        void ShowAscentMap(bool show){ascentMap=show;returnToMapAfterStage=false;RefreshLaunch();}
        void BeginStagingCloseup(){
            if(seenSeparation==launch.SeparationRevision)return;
            seenSeparation=launch.SeparationRevision;
            if(!stageCloseups)return;
            returnToMapAfterStage|=ascentMap;
            ascentMap=false;
            launchScene.PadCamera=false;
            closeupRemaining=Scene.PayloadRevealDuration;
        }
        void EndStagingCloseup(){
            if(returnToMapAfterStage&&!launch.InOrbit)ascentMap=true;
            returnToMapAfterStage=false;
            closeupRemaining=0;
        }
        void LaunchKeys(KeyEventArgs e) {
            if(e.OriginalSource is ComboBox || e.OriginalSource is ComboBoxItem)return;
            switch(e.Key){
                case Key.M:ShowAscentMap(!ascentMap);break;
                case Key.F9:ShowAscentMap(!ascentMap);break;
                case Key.Enter:if(!e.IsRepeat&&!launch.Started)LaunchCommand();break;
                case Key.P:launchRate=launchRate==0?1:0;launchSpeed.SelectedIndex=launchRate==0?0:1;break;
                case Key.Space:if(!e.IsRepeat)launch.Separate();break;
                case Key.F:if(!e.IsRepeat)launch.JettisonFairing();break;
                case Key.W:case Key.S:case Key.A:case Key.D:case Key.Up:case Key.Down:
                    if(launch.Auto){launch.Auto=false;guidance.IsChecked=false;}
                    e.Handled=true;return;
                case Key.Z:launch.Auto=false;guidance.IsChecked=false;launch.Throttle=1;break;
                case Key.X:launch.Auto=false;guidance.IsChecked=false;launch.Throttle=0;break;
                default:return;
            }
            e.Handled=true;RefreshLaunch();
        }
        Button launchGuided;
        void ToggleGuidedAscent(){
            bool next=!launch.Auto;
            launch.Auto=launch.AutoStage=next;
            guidance.IsChecked=autoStage.IsChecked=next;
            RefreshLaunch();
        }
        void ReadAscentControls(double elapsed){
            if(launch.Auto||launch.Failed||launch.InOrbit||!IsActive||
               Keyboard.FocusedElement is ComboBox||Keyboard.FocusedElement is ComboBoxItem)return;
            double pitch=Axis(Key.W,Key.S),heading=Axis(Key.D,Key.A),power=Axis(Key.Up,Key.Down);
            if(pitch==0&&heading==0&&power==0)return;
            double sensitivity=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?.24:1;
            launch.Pitch=Math.Max(-45,Math.Min(105,launch.Pitch+pitch*18*elapsed*sensitivity));
            launch.HeadingOffset=Math.Max(-180,Math.Min(180,launch.HeadingOffset+heading*23*elapsed*sensitivity));
            launch.Throttle=Math.Max(0,Math.Min(1,launch.Throttle+power*.58*elapsed*sensitivity));
            if(!launch.Started||launchRate==0)RefreshLaunch();
        }
        void RefreshLaunch() {
            if(screen!="launch")return;
            BeginStagingCloseup();
            rocketViewport.Visibility=ascentMap?Visibility.Collapsed:Visibility.Visible;
            mapViewport.Visibility=ascentMap?Visibility.Visible:Visibility.Collapsed;
            if(ascentMap)ascentMapScene.SetFlight(launchTarget,Orbit.FromState(launch.Mission.Body,launch.Position,launch.Velocity,launch.Time),launch.Position);
            else launchScene.LaunchView(launch,vehicle);
            ascentChart.InvalidateVisual();
            if(launchSky!=null)launchSky.Opacity=Math.Exp(-Math.Max(0,launch.Altitude)/18);
            syncing=true;throttle.Value=launch.Throttle*100;steering.Value=launch.Pitch;syncing=false;
            throttle.IsEnabled=steering.IsEnabled=!launch.Auto&&!launch.Failed;
            stageButton.Content=launch.BoostersAttached?"Jettison boosters   [Space]":launch.Stage==0?"Separate core   [Space]":"Release payload   [Space]";
            stageButton.IsEnabled=launch.CanSeparate;
            fairingButton.Visibility=launch.Payload>0?Visibility.Visible:Visibility.Collapsed;
            fairingButton.IsEnabled=launch.CanSeparate&&launch.FairingAttached;
            fairingButton.Content=launch.FairingAttached?"Jettison fairing   [F]":"Fairing jettisoned";
            fairingButton.ToolTip=launch.FairingAttached&&!launch.Mission.Body.Lunar
                ?"Wait until the air thins to avoid aerodynamic loads on the spacecraft."
                :"Jettison the two protective shell halves.";
            ignite.IsEnabled=!launch.InOrbit&&!launch.Failed&&(!launch.Started||launch.Countdown<=0);
            ignite.Content=!launch.Started?"LAUNCH   [Enter]":launch.Countdown>0?"Launching…":launch.Throttle>0?"Cut liquid engine   [X]":"Full throttle   [Z]";
            launchMapButton.Content=ascentMap?"Rocket view   M":"Map   M";
            launchGuided.Content=launch.Auto?"Guidance ON":"Guided ascent";
            padCameraButton.Content=launchScene.PadCamera?"Tracking camera":"Pad camera";
            launchMessage.Text=launch.Started&&launch.Countdown>0?"IGNITION IN "+launch.Countdown.ToString("0.0")+" s":!launch.Started?"On pad · set throttle, then launch":launch.Status;
            launchMessage.Foreground=launch.Failed?Red:White;
            var estimate=Orbit.FromState(launch.Mission.Body,launch.Position,launch.Velocity,launch.Time);
            string ap=estimate==null?"escape":estimate.Ap.ToString("0.0");
            string pe=estimate==null?"—":estimate.Pe.ToString("0.0");
            bool inCloseup=closeupRemaining>0&&!launch.Failed;
            if(launch.InOrbit)launchMessage.Text=flight!=null&&flight.Finished?flight.Status:
                vehicle.Has(3)?"Spacecraft released · deploying solar panels":"Spacecraft released";
            if(launch.Started&&!launch.Failed&&launch.Countdown<=0&&!inCloseup&&!launch.InOrbit){
                if(launch.BoostersAttached&&launch.BoosterFuel<.02)launchMessage.Text="Boosters depleted · Space to jettison";
                else if(launch.Stage==0&&launch.CoreFuel<.02)launchMessage.Text="Core depleted · Space to separate";
                else if(launch.Stage==1&&estimate!=null&&estimate.Pe>=115)launchMessage.Text="Stable orbit · Space to release spacecraft";
                else if(launch.FairingAttached&&launch.AutoJettisonReady)launchMessage.Text="Fairing clear for jettison · F";
                else if(launch.HasLifted&&launch.Status=="Liftoff confirmed.")launchMessage.Text=launch.Auto?"Guidance assist engaged":"Manual flight";
            }
            var radial=launch.Position;radial.Normalize();
            var surfaceV=launch.HasLifted?launch.SurfaceVelocity:new Vector3D();
            double vertical=Vector3D.DotProduct(surfaceV,radial);
            double horizontal=Math.Sqrt(Math.Max(0,surfaceV.LengthSquared-vertical*vertical));
            launchAltitude.Text=Math.Max(0,launch.Altitude*1000).ToString("N0")+" m";
            launchVelocity.Text=(surfaceV.Length*1000).ToString("N0")+" m/s";
            launchVelocityDetail.Text=string.Format("VERT {0:+0;-0;0}   HORIZ {1:N0} m/s",vertical*1000,horizontal*1000);
            launchClock.Text=launch.Started&&launch.Countdown>0?"T− "+launch.Countdown.ToString("0.0")+" s":"MET "+TimeSpan.FromSeconds(launch.Time).ToString(@"hh\:mm\:ss");
            if(inCloseup&&launchRate>1)launchClock.Text+="  ·  STAGING 1×";
            launchStage.Text=launch.InOrbit?"03  SPACECRAFT RELEASED":
                (launch.Stage==0?"01  CORE STAGE":"02  UPPER STAGE")+(launch.Auto?"  /  GUIDED":"  /  MANUAL");
            if(!launch.Auto&&Math.Abs(launch.HeadingOffset)>.1)launchStage.Text+="\nHeading offset "+launch.HeadingOffset.ToString("+0;-0;0")+"°";
            launchOrbit.Text="Ap "+ap+" km  ·  Pe "+pe+" km\nEARTH TARGET "+launch.Mission.Pe.ToString("0")+" km / "+launch.Mission.Inclination.ToString("0.#")+"°";
            if(ascentMap)launchOrbit.Text+="\nCyan · target   Amber · predicted\nWhite marker · current position";
            double twr=launch.Started&&launch.Countdown<=0?launch.Acceleration:launch.Design.PadTwr(launch.Payload,launch.Mission.Body);
            launchFuelText.Text=launch.TotalMass.ToString("N0")+" kg  ·  TWR "+twr.ToString("0.00");
            coreGauge.Value=launch.Stage==0&&launch.Design.CoreLoad>0?launch.CoreFuel/launch.Design.CoreLoad:0;
            upperGauge.Value=launch.Design.UpperLoad>0?launch.UpperFuel/launch.Design.UpperLoad:0;
            boosterGauge.Value=launch.BoostersAttached&&launch.Design.AssistLoad>0?launch.BoosterFuel/launch.Design.AssistLoad:0;
            launchBall.Pitch=launch.Pitch;launchBall.Throttle=launch.Throttle;
            launchBall.VelocityPitch=surfaceV.Length>.001?Math.Atan2(vertical,horizontal)*180/Math.PI:double.NaN;launchBall.InvalidateVisual();
            launchReadout.Text=string.Format("Vertical speed  {0:+0;−0;0} m/s\nInertial speed  {1:0} m/s\nDynamic press.  {2:0.0} kPa\nCore fuel       {3:N0} kg\nBooster fuel    {4:N0} kg\nUpper fuel      {5:N0} kg\nSuggested pitch {6:0}°\nSuggested thrust {7:0}%",vertical*1000,launch.Velocity.Length*1000,launch.DynamicPressure/1000,launch.CoreFuel,launch.BoosterFuel,launch.UpperFuel,launch.SuggestedPitch,launch.SuggestedThrottle*100);
        }
        void UpdateLaunch(double dt){
            ReadAscentControls(dt);
            BeginStagingCloseup();
            if(launch.InOrbit&&stageCloseups&&launch.Payload>0&&launch.DeploymentViewSeconds<Scene.PayloadRevealDuration){
                if(flight==null)CreateDeployedFlight();
                // Coast during the close-up using the same flight state the player receives.
                flight.Tick(dt,0);
                launch.Position=flight.Position;launch.Velocity=flight.Velocity;launch.Time=flight.Time;
                launch.DeploymentViewSeconds+=dt;
                RefreshLaunch();return;
            }
            if(!launch.InOrbit&&(launchRate==0||!launch.Started||launch.Failed))return;
            if(!launch.InOrbit){
                bool wasCloseup=closeupRemaining>0;
                double rate=launch.Countdown>0||wasCloseup?Math.Min(launchRate,1):launchRate;
                launch.Tick(dt*rate,stageCloseups);
                if(wasCloseup){
                    closeupRemaining-=dt;
                    if(closeupRemaining<=0)EndStagingCloseup();
                }
                if(launch.InOrbit&&stageCloseups&&launch.Payload>0){RefreshLaunch();return;}
            }
            if(launch.InOrbit){
                if(flight==null)CreateDeployedFlight();
                if(hellasMission){
                    if(flight.Finished)return;
                    BeginMarsCruise();return;
                }
                preview=mission.Target;warp=1;follow=true;
                showOrbitTools=mission.Body.Lunar||!presetAuto;
                if(presetAuto&&launch.Auto){autoOrbitPhase=1;autoOrbitNext=watch.Elapsed.TotalSeconds+1.5;}
                FlightScreen(true);return;
            }
            RefreshLaunch();
        }
        void CreateDeployedFlight(){
            flight=new Flight(mission,vehicle,launch,!sandbox);
            paused=false;science=false;tx=false;
        }
        SystemMap AddNavigationMap(Grid host,SolarSystem current){
            var navigation=new SystemMap(current,mapFocus);
            host.Children.Add(navigation);
            var controls=new StackPanel{Orientation=Orientation.Horizontal};
            var focus=Options(SolarSystem.Names,mapFocus);
            focus.Width=106;
            focus.ToolTip="Choose camera focus or right-click a body on the map.";
            focus.SelectionChanged+=(sender,args)=>{
                if(focus.SelectedIndex>=0&&navigation.Selected!=focus.SelectedIndex)navigation.FocusBody(focus.SelectedIndex);
            };
            navigation.FocusChanged=index=>{mapFocus=index;focus.SelectedIndex=index;};
            controls.Children.Add(focus);
            controls.Children.Add(FlightButton("Zoom +",()=>navigation.Zoom(.52)));
            controls.Children.Add(FlightButton("Zoom −",()=>navigation.Zoom(1.9)));
            controls.Children.Add(FlightButton("Moon range",navigation.ZoomToMoonDistance));
            controls.Children.Add(FlightButton("Planet range",navigation.ZoomToPlanets));
            controls.Children.Add(FlightButton("System zoom",navigation.ZoomToSystem));
            var strip=PlaceHud(host,controls,HorizontalAlignment.Left,VerticalAlignment.Top,new Thickness(12,72,0,0),double.NaN,5);
            strip.ToolTip="Right-click a celestial body to focus it. Scroll to zoom; drag to rotate.";
            return navigation;
        }
        void OpenSystemMap(Action back,SolarSystem current){
            Clear("system");
            var host=new Grid();baseGrid.Children.Add(host);
            var navigation=AddNavigationMap(host,current);
            if(mission!=null)navigation.SetFlight(mission.Target,null,null);
            var controls=new StackPanel{Orientation=Orientation.Horizontal};
            Add(controls,FlightButton("‹  Return",back));
            Add(controls,T("Navigation map",18,White,true));
            PlaceHud(host,controls,HorizontalAlignment.Stretch,VerticalAlignment.Top,new Thickness(10,9,10,0),double.NaN,8);
        }
    }
}

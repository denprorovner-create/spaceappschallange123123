using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace Perilune {
    sealed class SystemMap : FrameworkElement {
        readonly SolarSystem solar;
        readonly Dictionary<int,Point> targets=new Dictionary<int,Point>();
        readonly Dictionary<int,Rect> labels=new Dictionary<int,Rect>();
        readonly Dictionary<int,BitmapSource> textures=new Dictionary<int,BitmapSource>();
        readonly Dictionary<int,byte[]> pixels=new Dictionary<int,byte[]>();
        readonly Dictionary<int,Orbit> bodyOrbits=new Dictionary<int,Orbit>();
        double yaw=.15, tilt=.51, span=18000, orbitEpoch=double.NegativeInfinity;
        Point? drag;
        Orbit planned,actual;
        Vector3D? ship;
        int shipPrimary=3, targetPrimary=3;
        LunarTransfer transfer;
        Vector3D? marsShip;
        Vector3D[] marsPrediction;
        IReadOnlyList<Vector3D> marsHistory;
        public int Selected {get;private set;}
        public Action<int> FocusChanged;
        public static readonly double[] Radii={695700,2439.7,6051.8,6378.14,1737.4,3396.2,71492,60268,25559,24764};
        static readonly int[] palette={0xf4cc73,0xa69c92,0xd9b87d,0x79b7e1,0xbabbbb,0xc78362,0xc9ab8f,0xd6be90,0x89c9c8,0x6587c2};
        static Brush Paint(int c){var b=new SolidColorBrush(Color.FromRgb((byte)(c>>16),(byte)(c>>8),(byte)c));b.Freeze();return b;}
        static readonly Brush Ink=Paint(0x090f19), Paper=Paint(0xe3edfb), Quiet=Paint(0x91aabc), Plan=Paint(0x79d5d9), Track=Paint(0xffc977), Star=Paint(0x31455d);
        public SystemMap(SolarSystem solar,int focus=3){
            this.solar=solar;Selected=focus;span=DefaultSpan(focus);ClipToBounds=true;
            ToolTip="Drag to rotate. Scroll to zoom. Right-click a world to focus it.";
            MouseLeftButtonDown+=(s,e)=>{drag=e.GetPosition(this);CaptureMouse();};
            MouseLeftButtonUp+=(s,e)=>{drag=null;ReleaseMouseCapture();};
            LostMouseCapture+=(s,e)=>drag=null;
            MouseMove+=(s,e)=>{
                if(!drag.HasValue||!IsMouseCaptured)return;
                var p=e.GetPosition(this);
                yaw-=(p.X-drag.Value.X)*.008;
                tilt=Math.Max(-1.48,Math.Min(1.48,tilt+(p.Y-drag.Value.Y)*.008));
                drag=p;InvalidateVisual();
            };
            MouseWheel+=(s,e)=>{Zoom(e.Delta>0?1/1.58:1.58);e.Handled=true;};
            MouseRightButtonUp+=(s,e)=>e.Handled=FocusAt(e.GetPosition(this));
        }
        public bool FocusAt(Point position){
            int nearest=-1;double best=double.PositiveInfinity;
            foreach(var target in targets){
                double distance=(target.Value-position).Length;
                if(labels.ContainsKey(target.Key)&&labels[target.Key].Contains(position))distance=0;
                if(distance>Math.Max(18,BodyDisk(target.Key))||distance>=best)continue;
                nearest=target.Key;best=distance;
            }
            if(nearest<0)return false;
            FocusBody(nearest);return true;
        }
        static double DefaultSpan(int target){return target==0?5.1e9:Math.Max(5600,Radii[target]*2.65);}
        public void Zoom(double factor){span=Math.Max(Radii[Selected]*1.1,Math.Min(5.5e9,span*factor));InvalidateVisual();}
        public void ZoomToSystem(){span=5.1e9;InvalidateVisual();}
        public void ZoomToPlanets(){span=4.0*SolarSystem.AU;InvalidateVisual();}
        public void ZoomToMoonDistance(){span=470000;InvalidateVisual();}
        public void FocusBody(int index){
            if(index<0||index>=SolarSystem.Names.Length)return;
            Selected=index;span=DefaultSpan(index);
            if(FocusChanged!=null)FocusChanged(index);
            InvalidateVisual();
        }
        public void SetFlight(Orbit target,Orbit current,Vector3D? position,Body reference=null,LunarTransfer plan=null){
            planned=target;actual=current;ship=position;transfer=plan;
            if(target!=null)targetPrimary=target.Body.Lunar?4:3;
            shipPrimary=reference==null?targetPrimary:reference.Lunar?4:3;
            InvalidateVisual();
        }
        public void SetMarsFlight(Vector3D? current,Vector3D[] prediction,IReadOnlyList<Vector3D> history=null){
            marsShip=current;marsPrediction=prediction;marsHistory=history;
            InvalidateVisual();
        }
        SolarSystem activeSolar;
        public void SetClock(SolarSystem clock){activeSolar=clock;InvalidateVisual();}
        SolarSystem Clock {get{return activeSolar??solar;}}
        double Scale { get { return Math.Max(1,Math.Min(ActualWidth,ActualHeight))*.46/span; } }
        Point Offset(Vector3D p){
            double right=p.X*Math.Cos(yaw)-p.Z*Math.Sin(yaw);
            double depth=p.X*Math.Sin(yaw)+p.Z*Math.Cos(yaw);
            double up=p.Y*Math.Cos(tilt)-depth*Math.Sin(tilt);
            return new Point(ActualWidth*.5+right*Scale,ActualHeight*.47-up*Scale);
        }
        Point Project(Vector3D absolute){return Offset(absolute-Clock.Positions[Selected]);}
        void DrawMarsFlight(DrawingContext dc){
            var origin=Clock.Positions[0];
            if(marsPrediction!=null&&marsPrediction.Length>1){
                var track=new StreamGeometry();
                using(var line=track.Open()){
                    line.BeginFigure(Project(origin+marsPrediction[0]),false,false);
                    for(int i=1;i<marsPrediction.Length;i++)line.LineTo(Project(origin+marsPrediction[i]),true,false);
                }
                dc.DrawGeometry(null,new Pen(Plan,1.7),track);
            }
            if(marsHistory!=null&&marsHistory.Count>1){
                var flown=new StreamGeometry();
                using(var line=flown.Open()){
                    line.BeginFigure(Project(origin+marsHistory[0]),false,false);
                    for(int i=1;i<marsHistory.Count;i++)line.LineTo(Project(origin+marsHistory[i]),true,false);
                    if(marsShip.HasValue)line.LineTo(Project(origin+marsShip.Value),true,false);
                }
                dc.DrawGeometry(null,new Pen(Track,2.0),flown);
            }
            if(marsShip.HasValue){
                var p=Project(origin+marsShip.Value);
                if(Visible(p,0)){
                    dc.DrawEllipse(Ink,new Pen(Track,1.5),p,6,6);
                    dc.DrawEllipse(Paper,null,p,2.4,2.4);
                }
            }
        }
        double CameraDepth(Vector3D p){
            return p.Y*Math.Sin(tilt)+(p.X*Math.Sin(yaw)+p.Z*Math.Cos(yaw))*Math.Cos(tilt);
        }
        void DrawOrbit(DrawingContext dc,Orbit orbit,Vector3D origin,Brush color,double weight,bool nearOnly=false){
            if(orbit==null||orbit.A>1e11)return;
            var path=new StreamGeometry();
            using(var line=path.Open()){
                bool started=false;
                for(int k=0;k<=180;k++){
                    var position=orbit.AtAngle(Orbit.Circle*k/180);
                    if(position.Length<orbit.Body.Radius){started=false;continue;}
                    if(nearOnly&&CameraDepth(position+origin-Clock.Positions[Selected])<0){started=false;continue;}
                    var point=Project(origin+position);
                    if(!started){line.BeginFigure(point,false,false);started=true;}
                    else line.LineTo(point,true,false);
                }
            }
            dc.DrawGeometry(null,new Pen(color,weight),path);
        }
        void CalculateOrbits(){
            if(Math.Abs(orbitEpoch-Clock.Time)<600)return;
            orbitEpoch=Clock.Time;
            for(int i=1;i<SolarSystem.Names.Length;i++){
                int parent=i==4?3:0;
                var primary=new Body(SolarSystem.Names[parent],"",Radii[parent],SolarSystem.GM[parent]+SolarSystem.GM[i],1);
                bodyOrbits[i]=Orbit.FromState(primary,Clock.Positions[i]-Clock.Positions[parent],Clock.Velocities[i]-Clock.Velocities[parent],Clock.Time);
            }
        }
        BitmapSource Texture(int i){
            BitmapSource found;
            if(textures.TryGetValue(i,out found))return found;
            string name=i==3?"earth.jpg":i==4?"moon.jpg":i==5?"mars.jpg":null;
            string path=name==null?null:System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets",name);
            if(path==null||!System.IO.File.Exists(path))return null;
            var result=new FormatConvertedBitmap(new BitmapImage(new Uri(path)),PixelFormats.Bgra32,null,0);
            result.Freeze();textures[i]=result;return result;
        }
        int renderedWorld=-1;
        double renderedYaw,renderedTilt,renderedRotation;
        BitmapSource disk;
        BitmapSource Globe(int index,BitmapSource texture){
            double rotation=Clock.Time/(index==3?86164:index==4?2360592:88643)*Orbit.Circle;
            if(disk!=null&&index==renderedWorld&&Math.Abs(renderedYaw-yaw)<.022&&Math.Abs(renderedTilt-tilt)<.022&&Math.Abs(renderedRotation-rotation)<.027)return disk;
            const int resolution=256;
            byte[] original;
            int width=texture.PixelWidth,height=texture.PixelHeight;
            if(!pixels.TryGetValue(index,out original)){
                original=new byte[width*height*4];
                texture.CopyPixels(original,width*4,0);pixels[index]=original;
            }
            var output=new byte[resolution*resolution*4];
            double ct=Math.Cos(tilt),st=Math.Sin(tilt);
            for(int row=0;row<resolution;row++)for(int col=0;col<resolution;col++){
                double x=2*(col+.5)/resolution-1,y=1-2*(row+.5)/resolution;
                double depth=1-x*x-y*y;
                if(depth<0)continue;
                double normal=Math.Sqrt(depth);
                double latitude=Math.Asin(Math.Max(-1,Math.Min(1,y*ct+normal*st)));
                double z=-y*st+normal*ct;
                double longitude=Math.Atan2(x,z)+yaw+rotation-.46;
                int colTex=Math.Min(width-1,(int)(Orbit.Wrap(longitude+Math.PI)/Orbit.Circle*width));
                int rowTex=Math.Min(height-1,(int)((.5-latitude/Math.PI)*height));
                int old=(rowTex*width+colTex)*4,at=(row*resolution+col)*4;
                double light=.33+.67*Math.Max(0,normal*.82-x*.38+y*.30)+.04*(1-normal);
                for(int channel=0;channel<3;channel++)output[at+channel]=(byte)Math.Min(255,original[old+channel]*light);
                output[at+3]=255;
            }
            disk=BitmapSource.Create(resolution,resolution,96,96,PixelFormats.Bgra32,null,output,resolution*4);
            disk.Freeze();renderedWorld=index;renderedYaw=yaw;renderedTilt=tilt;renderedRotation=rotation;
            return disk;
        }
        bool Visible(Point p,double margin){return p.X>=-margin&&p.Y>=-margin&&p.X<=ActualWidth+margin&&p.Y<=ActualHeight+margin;}
        void Label(DrawingContext dc,int index,Point p,double radius){
            var name=new FormattedText(SolarSystem.Names[index],CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),12,Paper,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double x=p.X+Math.Min(radius,90)+12,y=p.Y-10;
            if(index==Selected&&radius>35){x=p.X-name.Width/2;y=p.Y+Math.Min(radius,ActualHeight*.33)+12;}
            var border=new Rect(x-5,y-3,name.Width+10,name.Height+6);labels[index]=border;
            dc.DrawRoundedRectangle(Ink,null,border,4,4);dc.DrawText(name,new Point(x,y));
        }
        void DrawBody(DrawingContext dc,int i,Point p){
            double r=BodyDisk(i);
            if(i==Selected)dc.DrawEllipse(null,new Pen(Plan,1),p,r+5,r+5);
            dc.DrawEllipse(Paint(palette[i]),null,p,r,r);
            var texture=r>23?Texture(i):null;
            if(texture!=null&&i==Selected){
                dc.DrawImage(Globe(i,texture),new Rect(p.X-r,p.Y-r,r*2,r*2));
                dc.DrawEllipse(null,new Pen(Paint(palette[i]),.65),p,r,r);
            }else if(texture!=null){
                dc.PushClip(new EllipseGeometry(p,r,r));
                dc.DrawImage(texture,new Rect(p.X-r*2,p.Y-r,r*4,r*2));
                dc.Pop();
            }
            if(i!=0&&r>10&&(texture==null||i!=Selected)){
                var shade=new RadialGradientBrush();
                shade.Center=new Point(.33,.27);shade.GradientOrigin=shade.Center;
                shade.RadiusX=.95;shade.RadiusY=.95;
                shade.GradientStops.Add(new GradientStop(Color.FromArgb(0,1,5,13),.32));
                shade.GradientStops.Add(new GradientStop(Color.FromArgb(220,1,5,13),1));
                dc.DrawEllipse(shade,new Pen(Paint(palette[i]),.7),p,r,r);
            }
            Label(dc,i,p,r);
            targets[i]=p;
        }
        double BodyDisk(int i){return Math.Max(i==Selected?7:4,Math.Min(ActualHeight*.46,Radii[i]*Scale));}
        protected override void OnRender(DrawingContext dc){
            dc.DrawRectangle(Ink,null,new Rect(RenderSize));
            var stars=new Random(131);
            for(int i=0;i<110;i++){
                double x=stars.NextDouble()*ActualWidth,y=stars.NextDouble()*ActualHeight;
                dc.DrawEllipse(i%11==0?Quiet:Star,null,new Point(x,y),1,i%11==0?1:.65);
            }
            if(ActualWidth<=1||ActualHeight<=1)return;
            targets.Clear();labels.Clear();CalculateOrbits();
            var orbitInk=Paint(0x3d5368);
            if(span>=8e6){
                for(int i=1;i<SolarSystem.Names.Length;i++)if(i!=4)DrawOrbit(dc,bodyOrbits[i],Clock.Positions[0],orbitInk,1);
            }else if(span>100000){
                DrawOrbit(dc,bodyOrbits[4],Clock.Positions[3],orbitInk,1);
            }
            if(span<3e6){
                var primary=Clock.Positions[shipPrimary];
                DrawOrbit(dc,planned,Clock.Positions[targetPrimary],Plan,1.65);
                DrawOrbit(dc,actual,primary,Track,1.45);
                if(transfer!=null&&shipPrimary==3&&transfer.Path!=null){
                    var path=new StreamGeometry();
                    using(var line=path.Open()){
                        line.BeginFigure(Project(primary+transfer.Path[0]),false,false);
                        for(int i=1;i<transfer.Path.Length;i++)line.LineTo(Project(primary+transfer.Path[i]),true,false);
                    }
                    dc.DrawGeometry(null,new Pen(Plan,1.5),path);
                    var rendezvous=Project(primary+transfer.MoonAtArrival);
                    if(Visible(rendezvous,0)){
                        dc.DrawEllipse(null,new Pen(Plan,1),rendezvous,7,7);
                        var caption=new FormattedText("Moon at arrival",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),11,Paper,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                        dc.DrawText(caption,new Point(rendezvous.X+10,rendezvous.Y-8));
                    }
                }
            }
            for(int i=0;i<SolarSystem.Names.Length;i++){
                if(i==Selected||(i==4&&span>1.7e7))continue;
                var p=Project(Clock.Positions[i]);
                if(Visible(p,16))DrawBody(dc,i,p);
            }
            DrawBody(dc,Selected,Project(Clock.Positions[Selected]));
            if(span<3e6){
                var primary=Clock.Positions[shipPrimary];
                if(targetPrimary==Selected)DrawOrbit(dc,planned,Clock.Positions[targetPrimary],Plan,1.65,true);
                if(shipPrimary==Selected)DrawOrbit(dc,actual,primary,Track,1.45,true);
                if(ship.HasValue){
                    var pos=Project(primary+ship.Value);
                    if(Visible(pos,0)){
                        var center=Project(Clock.Positions[Selected]);
                        bool occluded=CameraDepth(primary+ship.Value-Clock.Positions[Selected])<0&&(pos-center).Length<BodyDisk(Selected);
                        dc.DrawEllipse(Ink,new Pen(Track,1.4),pos,5.5,5.5);
                        if(!occluded)dc.DrawEllipse(Paper,null,pos,2.3,2.3);
                    }
                }
            }
            DrawMarsFlight(dc);
            double units=span>8e6?SolarSystem.AU:1;
            double length=100/Scale/units;
            double tick=Math.Pow(10,Math.Floor(Math.Log10(length)));
            double distance=Math.Max(tick,Math.Floor(length/tick)*tick);
            double px=distance*units*Scale;
            double left=ActualWidth-px-27,yScale=ActualHeight-29;
            var pen=new Pen(Quiet,1);
            dc.DrawLine(pen,new Point(left,yScale),new Point(left+px,yScale));
            dc.DrawLine(pen,new Point(left,yScale-4),new Point(left,yScale+4));
            dc.DrawLine(pen,new Point(left+px,yScale-4),new Point(left+px,yScale+4));
            var scaleText=new FormattedText(distance.ToString("0.##")+" "+(units==1?"km":"AU"),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),11,Quiet,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(scaleText,new Point(left,yScale-19));
        }
    }
}


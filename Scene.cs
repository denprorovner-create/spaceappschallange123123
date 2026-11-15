using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Collections.Generic;
using System.Globalization;

namespace Perilune {
    sealed partial class Scene : Viewport3D {
        static readonly System.Collections.Generic.Dictionary<Body,BitmapSource> surfaceImages=new System.Collections.Generic.Dictionary<Body,BitmapSource>();
        readonly PerspectiveCamera camera=new PerspectiveCamera();
        readonly PerspectiveCamera backdropCamera=new PerspectiveCamera{NearPlaneDistance=.00001,FarPlaneDistance=40};
        readonly Model3DGroup backdropWorld=new Model3DGroup();
        readonly DirectionalLight backdropSun=new DirectionalLight(Colors.White,new Vector3D(-1,0,0));
        Model3DGroup backdropGlobe;
        Body backdropBody;
        public Viewport3D Backdrop { get; private set; }
        const double KilometersPerModelUnit=.0035;
        readonly Model3DGroup world=new Model3DGroup();
        readonly Model3DGroup moving=new Model3DGroup();
        public double Yaw=26, Pitch=17, Zoom=1;
        public Action<string> PartPicked;
        readonly Dictionary<Model3D,string> partKeys=new Dictionary<Model3D,string>();
        readonly Dictionary<GeometryModel3D,Material> originalMaterials=new Dictionary<GeometryModel3D,Material>();
        string selectedPart;
        public string SelectedPart {
            get { return selectedPart; }
            set { selectedPart=value; Highlight(); }
        }
        public string HitPart(Point point) {
            string key=null;
            VisualTreeHelper.HitTest(this,null,result=>{
                var hit=result as RayMeshGeometry3DHitTestResult;
                if(hit!=null && partKeys.TryGetValue(hit.ModelHit,out key))return HitTestResultBehavior.Stop;
                return HitTestResultBehavior.Continue;
            },new PointHitTestParameters(point));
            return key;
        }
        void Highlight() {
            foreach(var pair in originalMaterials) {
                string key;
                if(selectedPart!=null && partKeys.TryGetValue(pair.Key,out key) && key==selectedPart) {
                    var m=new MaterialGroup();m.Children.Add(pair.Value);
                    m.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(85,76,198,214))));
                    pair.Key.Material=m; if(pair.Key.BackMaterial!=null)pair.Key.BackMaterial=m;
                } else { pair.Key.Material=pair.Value;if(pair.Key.BackMaterial!=null)pair.Key.BackMaterial=pair.Value; }
            }
        }
        void TagPart(Model3DGroup group,string key) {
            foreach(var child in group.Children) {
                var geometry=child as GeometryModel3D;
                if(geometry!=null){partKeys[geometry]=key;originalMaterials[geometry]=geometry.Material;}
                var nested=child as Model3DGroup;if(nested!=null)TagPart(nested,key);
            }
        }
        Point? drag;
        Point dragStart;
        bool dragged;
        Point3D aimPoint;
        double aimDistance;
        Vector3D? aimOutward;
        readonly Action refresh;
        Body body;
        bool hangar=true, chase;
        double radius;
        Model3DGroup globe, vehicle;
        GeometryModel3D flown, guide, node, periapsis, apoapsis;
        DirectionalLight primaryLight;
        bool tracesVisible;
        Model3DGroup mapMarker;
        Orbit renderedCurrent, renderedTarget;
        public Scene(Action refresh) {
            this.refresh=refresh;
            ClipToBounds=true;
            Camera=camera;
            Backdrop=new Viewport3D{Camera=backdropCamera,IsHitTestVisible=false,ClipToBounds=true,Visibility=Visibility.Collapsed};
            Backdrop.Children.Add(new ModelVisual3D{Content=backdropWorld});
            SizeChanged+=(s,e)=>{if(aimDistance>0){Aim(aimPoint,aimDistance,aimOutward);if(Backdrop.Visibility==Visibility.Visible)refresh();}};
            camera.FieldOfView=43;camera.NearPlaneDistance=.002;camera.FarPlaneDistance=350;
            Children.Add(new ModelVisual3D{Content=world});
            MouseLeftButtonDown+=(s,e)=>{drag=dragStart=e.GetPosition(this);dragged=false;CaptureMouse();};
            MouseLeftButtonUp+=(s,e)=>{
                bool click=drag.HasValue&&!dragged;drag=null;ReleaseMouseCapture();
                if(click && hangar && padRocket==null){var key=HitPart(e.GetPosition(this));SelectedPart=key;if(PartPicked!=null)PartPicked(key);}
            };
            LostMouseCapture+=(s,e)=>drag=null;
            MouseMove+=(s,e)=>{
                if(!drag.HasValue||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;
                Point p=e.GetPosition(this);
                if((p-dragStart).Length>4)dragged=true;
                if(!dragged)return;
                Yaw-=(p.X-drag.Value.X)*.43;Pitch=Math.Max(-85,Math.Min(85,Pitch+(p.Y-drag.Value.Y)*.35));
                drag=p;refresh();
            };
            MouseWheel+=(s,e)=>{Zoom=Math.Max(.35,Math.Min(4,Zoom*Math.Pow(.87,e.Delta/120.0)));refresh();};
        }
        static Color Rgb(int rgb) { return Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb); }
        static SolidColorBrush Brush(int rgb) { return new SolidColorBrush(Rgb(rgb)); }
        static Material Lit(int rgb, bool glossy=false) {
            if(!glossy)return new DiffuseMaterial(Brush(rgb));
            var group=new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(Brush(rgb)));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(115,220,238,255)),30));
            return group;
        }
        static Material Glow(int rgb) { return new EmissiveMaterial(Brush(rgb)); }
        static BitmapSource BodyImage(Body planet){
            BitmapSource bitmap;
            if(surfaceImages.TryGetValue(planet,out bitmap))return bitmap;
            var uri=new Uri(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets",planet.Texture));
            var loaded=new BitmapImage(uri);loaded.Freeze();
            surfaceImages[planet]=loaded;
            return loaded;
        }
        static Model3DGroup TexturedGlobe(Body planet){
            var group=new Model3DGroup();
            group.Children.Add(Model(Sphere(1),new DiffuseMaterial(new ImageBrush(BodyImage(planet))),true));
            return group;
        }
        void SyncPlanetBackdrop(Body planet,double time,SolarSystem system,Vector3D position,
            Vector3D localX,Vector3D localY,Vector3D localZ,double opacity,double kmPerUnit=KilometersPerModelUnit,Vector3D? sun=null){
            if(opacity<=0){Backdrop.Visibility=Visibility.Collapsed;return;}
            if(backdropBody!=planet){
                backdropBody=planet;backdropWorld.Children.Clear();
                backdropWorld.Children.Add(new AmbientLight(Rgb(planet==MarsGlobe?0x6b625c:0x343741)));
                backdropWorld.Children.Add(backdropSun);
                backdropGlobe=TexturedGlobe(planet);
                backdropWorld.Children.Add(backdropGlobe);
            }
            Func<Vector3D,Vector3D> toWorld=d=>localX*d.X+localY*d.Y+localZ*d.Z;
            backdropSun.Direction=-(sun??MissionEnvironment.SunVector(system,planet));
            backdropGlobe.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),-360*(time/planet.Day%1)));
            var offset=toWorld((Vector3D)camera.Position)*kmPerUnit;
            backdropCamera.Position=(Point3D)((position+offset)/planet.Radius);
            backdropCamera.LookDirection=toWorld(camera.LookDirection);
            backdropCamera.UpDirection=toWorld(camera.UpDirection);
            backdropCamera.FieldOfView=camera.FieldOfView;
            Backdrop.Opacity=opacity;
            Backdrop.Visibility=Visibility.Visible;
        }
        public void Reset(bool assembly) { Yaw=assembly?24:36;Pitch=assembly?8:25;Zoom=1; }
        void Init() {
            world.Children.Clear();
            partKeys.Clear();originalMaterials.Clear();
            world.Children.Add(new AmbientLight(Rgb(hangar?0x747d84:0x343c49)));
            primaryLight=new DirectionalLight(Rgb(0xfff0df),new Vector3D(-.7,-.55,-.5));
            world.Children.Add(primaryLight);
            if(hangar){
                world.Children.Add(new DirectionalLight(Rgb(0x8499b4),new Vector3D(.6,-.45,1)));
                world.Children.Add(new PointLight(Rgb(0xfff6de),new Point3D(-3,8,4)){Range=40,ConstantAttenuation=1,LinearAttenuation=.009});
            }
        }
        static GeometryModel3D Model(MeshGeometry3D mesh,Material material,bool doubleSided=false) {
            mesh.Freeze();
            var model=new GeometryModel3D(mesh,material);
            if(doubleSided)model.BackMaterial=material;
            return model;
        }
        static MeshGeometry3D Ring(double[] ys,double[] rs,int slices,double x,double z) {
            var m=new MeshGeometry3D();
            for(int row=0;row<ys.Length;row++)for(int j=0;j<=slices;j++){
                double a=2*Math.PI*j/slices;
                m.Positions.Add(new Point3D(x+rs[row]*Math.Cos(a),ys[row],z+rs[row]*Math.Sin(a)));
                int before=Math.Max(0,row-1),after=Math.Min(ys.Length-1,row+1);
                double dy=ys[after]-ys[before],dr=rs[after]-rs[before];
                var normal=new Vector3D(dy*Math.Cos(a),-dr,dy*Math.Sin(a));
                if(normal.LengthSquared<1e-14)normal=new Vector3D(Math.Cos(a),0,Math.Sin(a));normal.Normalize();m.Normals.Add(normal);
                if(row==0||j==0)continue;
                int i=m.Positions.Count-1, l=i-slices-1;
                foreach(int idx in new[]{l-1,i-1,l,l,i-1,i})m.TriangleIndices.Add(idx);
            }
            return m;
        }
        static void Lathe(Model3DGroup g,int color,double[] ys,double[] rs,double x=0,double z=0,int slices=32,bool shine=true) {
            g.Children.Add(Model(Ring(ys,rs,slices,x,z),Lit(color,shine),true));
        }
        static void Box(Model3DGroup g,double x,double y,double z,double w,double h,double d,int color) {
            var m=new MeshGeometry3D();
            Point3D[] pts={new Point3D(x-w/2,y-h/2,z-d/2),new Point3D(x+w/2,y-h/2,z-d/2),
                new Point3D(x+w/2,y+h/2,z-d/2),new Point3D(x-w/2,y+h/2,z-d/2),new Point3D(x-w/2,y-h/2,z+d/2),
                new Point3D(x+w/2,y-h/2,z+d/2),new Point3D(x+w/2,y+h/2,z+d/2),new Point3D(x-w/2,y+h/2,z+d/2)};
            int[] faces={0,3,2,1,4,5,6,7,0,4,7,3,1,2,6,5,0,1,5,4,3,7,6,2};
            for(int i=0;i<faces.Length;i+=4) {
                int start=m.Positions.Count;
                for(int j=0;j<4;j++)m.Positions.Add(pts[faces[i+j]]);
                foreach(int j in new[]{0,1,2,0,2,3})m.TriangleIndices.Add(start+j);
            }
            g.Children.Add(Model(m,Lit(color,true)));
        }
        static void Beam(Model3DGroup g,Point3D start,Point3D end,double width,int color) {
            var direction=end-start;double length=direction.Length;if(length<1e-8)return;
            var part=new Model3DGroup();Box(part,0,length/2,0,width,length,width,color);
            var axis=Vector3D.CrossProduct(new Vector3D(0,1,0),direction);
            if(axis.LengthSquared<1e-10)axis=new Vector3D(1,0,0);
            var transform=new Transform3DGroup();
            transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis,Vector3D.AngleBetween(new Vector3D(0,1,0),direction))));
            transform.Children.Add(new TranslateTransform3D(start.X,start.Y,start.Z));part.Transform=transform;g.Children.Add(part);
        }
        static void Label(Model3DGroup g,string text,double x,double y,double z,double width,double height,int foreground,int background) {
            var drawing=new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brush(background),null,new RectangleGeometry(new Rect(0,0,512,128))));
            var words=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Bahnschrift"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),68,Brush(foreground),1.0);
            drawing.Children.Add(new GeometryDrawing(Brush(foreground),null,words.BuildGeometry(new Point(18,22))));
            var mesh=new MeshGeometry3D();
            mesh.Positions=new Point3DCollection(new[]{new Point3D(x-width/2,y-height/2,z),new Point3D(x+width/2,y-height/2,z),new Point3D(x+width/2,y+height/2,z),new Point3D(x-width/2,y+height/2,z)});
            mesh.TextureCoordinates=new PointCollection(new[]{new Point(0,1),new Point(1,1),new Point(1,0),new Point(0,0)});
            mesh.TriangleIndices=new Int32Collection(new[]{0,1,2,0,2,3});
            g.Children.Add(Model(mesh,new DiffuseMaterial(new DrawingBrush(drawing)),true));
        }
        static void Rib(Model3DGroup g,double y,double radius,int color,double thickness=.05) {
            Lathe(g,color,new[]{y-thickness/2,y+thickness/2},new[]{radius,radius},slices:48);
        }
        static void EngineBell(Model3DGroup g,double x,double y,double z,double radius) {
            Lathe(g,0x4f575d,new[]{y,y+.08,y+.24,y+.38,y+.46},new[]{radius,radius*.97,radius*.54,radius*.36,radius*.44},x,z,48);
            Lathe(g,0x10171d,new[]{y-.002,y+.19,y+.34},new[]{radius*.92,radius*.50,radius*.29},x,z,48,false);
            for(int i=0;i<6;i++)Lathe(g,0x9a6d49,new[]{y+.09+i*.047,y+.105+i*.047},new[]{radius*(.92-i*.09),radius*(.9-i*.09)},x,z,48);
            Box(g,x+radius*.65,y+.43,z,.13,.28,.17,0xb5bdc0);
            Beam(g,new Point3D(x+radius*.7,y+.46,z),new Point3D(x+radius*.35,y+.20,z),.045,0xadae9f);
        }
        Model3DGroup Spacecraft(Vehicle v) {
            var g=new Model3DGroup();
            double r=.52+.12*Math.Max(0,v.Selections[1]);
            if(v.Has(1)) {
                var bus=new Model3DGroup();
                Lathe(bus,0xb8893f,new[]{-.50,-.42,.39,.46},new[]{r*.91,r,r,r*.91},slices:8);
                Lathe(bus,0xe0d9bc,new[]{-.51,-.48,-.39},new[]{0.0,r,r},slices:8);
                Lathe(bus,0x839099,new[]{.38,.46,.47},new[]{r,r,0.0},slices:8);
                Box(bus,0,.05,-r,.55,.52,.20,0xd1d8d9);
                for(int i=0;i<4;i++)Box(bus,0,.21-i*.11,-r-.12,.40,.035,.03,0x37424b);
                for(int i=0;i<8;i++) {
                    double angle=i*Math.PI/4;
                    Beam(bus,new Point3D(Math.Sin(angle)*r,-.39,Math.Cos(angle)*r),new Point3D(Math.Sin(angle)*r,.38,Math.Cos(angle)*r),.027,0xeddba4);
                }
                Label(bus,"PERILUNE",0,-.04,r+.003,.57,.16,0x283847,0xdedac7);
                TagPart(bus,"payload:1");g.Children.Add(bus);
            }
            if(v.Has(2)) {
                var propulsion=new Model3DGroup();
                EngineBell(propulsion,0,-.96,0,.24);
                for(int i=0;i<4;i++) {
                    double a=i*Math.PI/2;
                    Box(propulsion,Math.Cos(a)*(r+.04),-.3,Math.Sin(a)*(r+.04),.13,.18,.13,0x343e49);
                }
                TagPart(propulsion,"payload:2");g.Children.Add(propulsion);
            }
            if(v.Has(3)) {
                var power=new Model3DGroup();
                for(int side=-1;side<=1;side+=2) {
                    int count=v.Selections[3]==0?2:v.Selections[3]==1?6:8;
                    Box(power,side*(r+.17),.04,.02,.47,.045,.06,0xc6cdd1);
                    for(int wing=0;wing<(v.Selections[3]==2?2:1);wing++) {
                        double y=(wing==0?.04:-.57),w=count*.26;
                        Box(power,side*(r+.23+w/2),y,.06,w,.51,.035,0xc3bfb0);
                        for(int i=0;i<count;i++)for(int j=0;j<3;j++)
                            Box(power,side*(r+.23+(i+.5)*.26),y+(j-1)*.153,.087,.239,.134,.015,0x153f72+(i+j)%3*0x020508);
                        for(int i=1;i<count;i++)Box(power,side*(r+.23+i*.26),y,.098,.008,.48,.006,0xb7b5a2);
                    }
                }
                TagPart(power,"payload:3");g.Children.Add(power);
            }
            for(int instrument=0;instrument<5;instrument++)if(v.HasInstrument(instrument)) {
                var payload=new Model3DGroup();
                double x=(instrument%2==0?-.27:.27),z=instrument<2?.28:-.25;
                if(instrument<2) {
                    Box(payload,x,.59,z,.41,.28,.37,instrument==0?0xd5d7cd:0xa17d44);
                    Lathe(payload,0x303944,new[]{.64,.99,1.03},new[]{.18,.18,.20},x,z);
                    Lathe(payload,0x112b45,new[]{1.035,1.04},new[]{.185,0.0},x,z);
                    Rib(payload,1.02,.2,0x8797a4);
                } else if(instrument==2) {
                    Box(payload,-r-.17,.49,-.3,.28,.19,.36,0xcfc8b4);
                    Box(payload,-r-.7,.49,-.3,1.02,.055,.72,0x9f9b86);
                    for(int i=0;i<8;i++)Box(payload,-r-.27-i*.12,.522,-.3,.025,.011,.66,0xede4c4);
                } else if(instrument==3) {
                    Lathe(payload,0xc3ccd2,new[]{.5,.85,.95},new[]{.055,.055,.34},.28,-.28);
                    Lathe(payload,0x283b4f,new[]{.953,.96},new[]{.32,0.0},.28,-.28);
                } else {
                    Beam(payload,new Point3D(.2,.25,-r),new Point3D(.6,.75,-2.0),.025,0xbcc9c8);
                    Box(payload,.6,.75,-2.0,.12,.14,.12,0xa89a6f);
                }
                TagPart(payload,"payload:5:"+instrument);g.Children.Add(payload);
            }
            if(v.Has(4)) {
                var comm=new Model3DGroup();
                if(v.Selections[4]>0) {
                    double dish=v.Selections[4]==1?.40:.58;
                    Lathe(comm,0x8e9eaa,new[]{.43,.72,.93},new[]{.065,.06,.055},.36,-.19);
                    Lathe(comm,0xd7dacf,new[]{.89,.94,1.02,1.13},new[]{0.0,dish*.38,dish*.74,dish},.36,-.19);
                    Lathe(comm,0x304657,new[]{.92,1.17,1.205},new[]{.042,.028,.048},.36,-.19);
                    for(int i=0;i<3;i++) {
                        double a=i*Math.PI*2/3;
                        Beam(comm,new Point3D(.36+dish*Math.Cos(a),1.12,-.19+dish*Math.Sin(a)),new Point3D(.36,1.31,-.19),.012,0x7b858b);
                    }
                } else Box(comm,.19,.09,r,.32,.35,.10,0xf0c676);
                TagPart(comm,"payload:4");g.Children.Add(comm);
            }
            return g;
        }
        static double CoreTop(Vehicle v) { return -2.65+v.Stack.CoreTanks*2.25; }
        static double RocketTop(Vehicle v,int stage=0) {
            double top=CoreTop(v)+.8+v.Stack.UpperTanks*.90+1.2;
            return stage==0?top:top-1.85-CoreTop(v);
        }
        Model3DGroup Rocket(Vehicle v,int stage=0,bool boosters=true,bool fairing=true,bool payload=true,bool coreOnly=false) {
            var g=new Model3DGroup();
            double r=.44+Math.Max(0,v.Stack.Core)*.09,coreTop=CoreTop(v);
            double shift=stage==1?-1.85-coreTop:0;
            if(stage==0 && v.Stack.Core>=0) {
                var coreEngine=new Model3DGroup();
                Lathe(coreEngine,0xc9cdcd,new[]{-2.79,-2.65},new[]{r*.87,r});
                EngineBell(coreEngine,0,-3.23,0,r*.76);
                for(int i=0;i<4;i++) {
                    double a=i*Math.PI/2;
                    Beam(coreEngine,new Point3D(Math.Cos(a)*r*.76,-2.77,Math.Sin(a)*r*.76),new Point3D(Math.Cos(a)*r*.45,-3.05,Math.Sin(a)*r*.45),.04,0x909a9c);
                }
                TagPart(coreEngine,"core");g.Children.Add(coreEngine);
            }
            if(stage==0 && v.Stack.CoreTanks>0) {
                var tanks=new Model3DGroup();
                for(int tank=0;tank<v.Stack.CoreTanks;tank++) {
                    double bottom=-2.65+tank*2.25,top=bottom+2.25;
                    Lathe(tanks,v.Stack.Core==2?0xb88549:0xe0e1d8,new[]{bottom,bottom+.10,top-.10,top},new[]{r*.96,r,r,r*.96},slices:48);
                    Rib(tanks,bottom+.07,r*1.012,0x566068,.065);Rib(tanks,top-.08,r*1.012,0x566068,.05);
                    for(int line=0;line<8;line++) {
                        double a=line*Math.PI/4;
                        Beam(tanks,new Point3D(Math.Cos(a)*r*1.001,bottom+.14,Math.Sin(a)*r*1.001),new Point3D(Math.Cos(a)*r*1.001,top-.14,Math.Sin(a)*r*1.001),.008,0xa7aeac);
                    }
                    if(tank==0) {
                        for(int i=0;i<4;i++) {
                            double a=i*Math.PI/2;
                            Box(tanks,Math.Sin(a)*r*.99,bottom+.42,Math.Cos(a)*r*.99,.16,.43,.05,0x303b40);
                        }
                        Label(tanks,"PERILUNE",0,bottom+1.3,r+.006,r*1.5,.2,0x243445,0xdedfd7);
                        Label(tanks,"01",0,bottom+1.63,r+.009,.26,.17,0xa24830,0xdedfd7);
                    }
                }
                // External feed line follows the actual tank stack.
                Box(tanks,r+.025,(-2.65+coreTop)/2,0,.055,coreTop+2.65,.065,0xd2d1bd);
                TagPart(tanks,"core fuel");g.Children.Add(tanks);
            }
            if(v.Stack.Engine>=0&&!coreOnly) {
                var enginePart=new Model3DGroup();
                EngineBell(enginePart,0,coreTop+.04+shift,0,r*.74);
                TagPart(enginePart,"upper");g.Children.Add(enginePart);
            }
            if(v.Stack.UpperTanks>0&&!coreOnly) {
                var tanks=new Model3DGroup();
                for(int tank=0;tank<v.Stack.UpperTanks;tank++) {
                    double b=coreTop+.53+tank*.90+shift;
                    Lathe(tanks,v.Stack.Engine==2?0xd5c5a6:0xd8ddda,new[]{b,b+.09,b+.80,b+.90},new[]{r*.90,r,r,r*.90},slices:48);
                    Rib(tanks,b+.06,r*1.012,0x576876,.045);Rib(tanks,b+.84,r*1.012,0x576876,.045);
                    for(int rib=0;rib<8;rib++) {
                        double a=rib*Math.PI/4;
                        Beam(tanks,new Point3D(Math.Sin(a)*r*1.002,b+.12,Math.Cos(a)*r*1.002),new Point3D(Math.Sin(a)*r*1.002,b+.75,Math.Cos(a)*r*1.002),.012,0xa4afae);
                    }
                }
                TagPart(tanks,"upper fuel");g.Children.Add(tanks);
            }
            if(v.Wet>0&&!coreOnly)MountPayload(g,v,stage,fairing,payload);
            if(stage==0 && boosters && v.Stack.Booster>0) {
                var sideParts=new Model3DGroup();
                for(int mount=0;mount<v.Stack.BoosterPairs;mount++)foreach(int side in new[]{-1,1}) {
                    double x=mount==0?side*(r+.40):0,z=mount==1?side*(r+.40):0;
                    double br=v.Stack.Booster==1?.27:.32;
                    Lathe(sideParts,0xd5d8ce,new[]{-2.82,-2.68,-.26,-.03,.36},new[]{br*.86,br,br,br*.76,0.0},x,z,40);
                    EngineBell(sideParts,x,-3.25,z,br*.76);
                    for(int ring=0;ring<5;ring++)Lathe(sideParts,ring==0?0xaf743e:0x79817e,new[]{-2.62+ring*.56,-2.59+ring*.56},new[]{br*1.014,br*1.014},x,z,40);
                    foreach(double y in new[]{-2.13,-.61})Beam(sideParts,new Point3D(x*.62,y,z*.62),new Point3D(x,y,z),.09,0x707c81);
                }
                TagPart(sideParts,"booster");g.Children.Add(sideParts);
            }
            return g;
        }
        static Model3DGroup Hangar(double floor,double ceiling,bool small) {
            var h=new Model3DGroup();double width=small?5.2:8.8,depth=small?5.2:9.3;
            Box(h,0,floor-.15,0,width*2,.30,depth*2.4,0x41494d);
            // Concrete slabs and inset inspection rails.
            for(int i=-5;i<=5;i++) {
                Box(h,i*width/5,floor+.006,0,.014,.008,depth*2,0x30393c);
                Box(h,0,floor+.007,i*depth/5,width*2,.008,.014,0x30393c);
            }
            for(int side=-1;side<=1;side+=2) {
                Box(h,side*1.85,floor+.012,1.2,.07,.012,depth*1.4,0xd5bb60);
                Box(h,side*2.13,floor+.013,1.2,.032,.012,depth*1.4,0xb6b9ad);
                for(int i=0;i<8;i++)Box(h,side*(2.25+i*.14),floor+.017,-1.8,.09,.012,.47,i%2==0?0xd5ba58:0x363b39);
            }
            Box(h,0,floor+.014,3.1,4.0,.012,.055,0xd2d1bc);
            Box(h,0,floor+.014,-2.2,4.0,.012,.055,0xd2d1bc);
            Box(h,0,(floor+ceiling)/2,-depth,width*2,ceiling-floor,.15,0x4b555b);
            // Rear wall steel bays, translucent clerestory glazing and cross bracing.
            for(int bay=-3;bay<=3;bay++) {
                double x=bay*width/3.5;
                Box(h,x,(floor+ceiling)/2,-depth+.14,.18,ceiling-floor,.22,0x899197);
                for(int level=0;level<4;level++) {
                    double y=floor+(ceiling-floor)*(level+.5)/4;
                    double section=(ceiling-floor)/4;
                    if(level>=2)Box(h,x+width/7,y,-depth+.11,width/3.5-.15,section-.18,.03,0xabbfca);
                    Beam(h,new Point3D(x,floor+level*section,-depth+.28),new Point3D(x+width/3.5,floor+(level+1)*section,-depth+.28),.065,0x68747b);
                    Beam(h,new Point3D(x+width/3.5,floor+level*section,-depth+.29),new Point3D(x,floor+(level+1)*section,-depth+.29),.065,0x68747b);
                }
            }
            for(int side=-1;side<=1;side+=2) {
                for(int bay=0;bay<4;bay++) {
                    double z=-depth+bay*depth*.52;
                    Box(h,side*width,(floor+ceiling)/2,z,.22,ceiling-floor,.22,0x76838b);
                    Beam(h,new Point3D(side*width,floor,z),new Point3D(side*width,ceiling,z+depth*.5),.10,0x57646a);
                    for(int level=1;level<4;level++) {
                        double y=floor+(ceiling-floor)*level/4;
                        Box(h,side*(width-.3),y,z+depth*.25,.68,.10,depth*.5,0x8c8f84);
                        Box(h,side*(width-.62),y+.36,z+depth*.25,.035,.035,depth*.5,0xd0ac4a);
                        for(int i=0;i<5;i++)Box(h,side*(width-.62),y+.18,z+i*depth*.125,.035,.36,.035,0xc4a347);
                    }
                }
            }
            for(int girder=0;girder<5;girder++) {
                double z=-depth+girder*depth*.45;
                Box(h,0,ceiling,z,width*2,.17,.20,0x7c898f);
                for(int i=-4;i<4;i++) {
                    Beam(h,new Point3D(i*width/4,ceiling,z),new Point3D((i+.5)*width/4,ceiling-.45,z),.06,0x58646a);
                    Beam(h,new Point3D((i+.5)*width/4,ceiling-.45,z),new Point3D((i+1)*width/4,ceiling,z),.06,0x58646a);
                }
                Box(h,0,ceiling-.45,z,width*2,.065,.065,0x58646a);
                for(int side=-1;side<=1;side+=2) {
                    Box(h,side*width*.53,ceiling-.57,z,1.6,.06,.32,0xe4e5cf);
                    var lamp=Model(Ring(new[]{ceiling-.61,ceiling-.60},new[]{.24,0.0},16,side*width*.53,z),Glow(0xffefc1),true);h.Children.Add(lamp);
                }
            }
            // High bay crane, rolling door and service equipment ground the scale.
            Box(h,0,ceiling-1.1,-depth*.62,width*1.8,.34,.48,0xb59a42);
            Box(h,-2.4,ceiling-1.4,-depth*.62,.62,.40,.62,0x777f7a);
            Beam(h,new Point3D(-2.4,ceiling-1.6,-depth*.62),new Point3D(-2.4,ceiling-2.4,-depth*.62),.025,0x292f31);
            Box(h,0,floor+2,-depth+.34,3.6,4,.15,0x737975);
            for(int i=0;i<13;i++)Box(h,0,floor+.18+i*.3,-depth+.44,3.55,.025,.035,0x4e5b5b);
            Label(h,small?"PAYLOAD LAB":"ASSEMBLY  04",0,floor+4.5,-depth+.48,3.8,.68,0xe5e6d8,0x354149);
            Label(h,"PERILUNE",width*.56,ceiling*.57,-depth+.33,3.3,.70,0xf1e9cb,0x4b555b);
            for(int side=-1;side<=1;side+=2) {
                Box(h,side*(width-1.3),floor+.42,-depth+1.1,1.4,.82,.75,0x778386);
                for(int i=0;i<4;i++)Box(h,side*(width-1.3),floor+.18+i*.17,-depth+1.49,1.27,.11,.025,0x424c50);
                Box(h,side*(width-1.3),floor+.91,-depth+1.1,1.45,.09,.80,0xd4b754);
            }
            return h;
        }
        double assemblyAimY=-.1,assemblyDistance=10.6;
        public void Assembly(Vehicle v,bool stack) {
            hangar=true;body=null;padRocket=null;Init();
            camera.NearPlaneDistance=.15;
            var g=stack?Rocket(v):Spacecraft(v);
            double floor=stack?-3.30:-1.08,top=stack?RocketTop(v):1.25;
            world.Children.Add(Hangar(floor,Math.Max(9,top+3),!stack));
            // Maintenance platform and four retractable hold-down clamps.
            var stand=new Model3DGroup();
            if(stack) {
                Box(stand,0,floor-.03,0,2.65,.16,2.65,0x69767b);
                for(int side=-1;side<=1;side+=2) {
                    Box(stand,side*.75,floor+.24,0,.22,.5,.50,0x777e76);
                    Box(stand,side*.64,floor+.48,0,.34,.10,.43,0xa58945);
                }
            } else {
                Lathe(stand,0x697a83,new[]{floor-.03,floor+.02},new[]{1.02,1.02},slices:64);
                for(int side=-1;side<=1;side+=2)Beam(stand,new Point3D(side*.8,floor,0),new Point3D(side*.5,-.55,0),.06,0xc8bb85);
            }
            world.Children.Add(stand);world.Children.Add(g);Highlight();
            assemblyAimY=stack?(floor+top)/2:.1;
            assemblyDistance=stack?Math.Max(10.5,(top-floor)*1.75):7.3;
            Aim(new Point3D(0,assemblyAimY,0),assemblyDistance);
        }
        public void AssemblyView(bool stack) { Aim(new Point3D(0,assemblyAimY,0),assemblyDistance); }
        static MeshGeometry3D Sphere(double r) {
            var m=new MeshGeometry3D();
            const int rows=72,cols=144;
            for(int j=0;j<=rows;j++)for(int i=0;i<=cols;i++){
                double v=j/(double)rows,u=i/(double)cols,lat=(.5-v)*Math.PI,lon=(u-.5)*2*Math.PI;
                var n=new Vector3D(Math.Cos(lat)*Math.Cos(lon),Math.Sin(lat),Math.Cos(lat)*Math.Sin(lon));
                m.Positions.Add((Point3D)(n*r));m.Normals.Add(n);
                m.TextureCoordinates.Add(new Point(u,v));
                if(j==0||i==0)continue;
                int k=m.Positions.Count-1,l=k-cols-1;
                foreach(int index in new[]{l-1,l,k-1,l,k,k-1})m.TriangleIndices.Add(index);
            }
            return m;
        }
        static MeshGeometry3D Trace(Orbit o,double width) {
            var m=new MeshGeometry3D();
            if(o==null)return m;
            double scale=1/o.Body.Radius;
            bool priorAbove=false;
            for(int i=0;i<=384;i++){
                double angle=i*Math.PI/192;
                var p=o.AtAngle(angle)*scale;
                bool above=p.Length>=1;
                var tangent=o.AtAngle(angle+.001)-o.AtAngle(angle-.001);
                var normal=Vector3D.CrossProduct(tangent,o.Rotate(0,1));
                if(normal.LengthSquared<1e-8)normal=Vector3D.CrossProduct(tangent,o.Rotate(1,0));
                normal.Normalize();var side=Vector3D.CrossProduct(tangent,normal);side.Normalize();
                for(int j=0;j<4;j++){
                    double phase=j*Math.PI/2;
                    m.Positions.Add((Point3D)(p+width*(normal*Math.Cos(phase)+side*Math.Sin(phase))));
                }
                if(i>0&&above&&priorAbove)for(int j=0;j<4;j++){
                    int a=(i-1)*4+j,b=(i-1)*4+(j+1)%4;
                    foreach(int idx in new[]{a,b,a+4,b,b+4,a+4})m.TriangleIndices.Add(idx);
                }
                priorAbove=above;
            }
            return m;
        }
        public void FlightView(Orbit target,Flight flight,bool follow,Vehicle v,Ascent launch=null) {
            if(body!=target.Body || hangar) {
                hangar=false;body=target.Body;Init();
                globe=TexturedGlobe(body);
                world.Children.Add(globe);
                guide=Model(new MeshGeometry3D(),Glow(0x3aa9c6),true);
                flown=Model(new MeshGeometry3D(),Glow(0xf29a4e),true);
                node=Model(Sphere(.018),Glow(0xf4a762),true);
                periapsis=Model(Sphere(.011),Glow(0xe6e5d8),true);
                apoapsis=Model(Sphere(.011),Glow(0xe6e5d8),true);
                mapMarker=new Model3DGroup();
                mapMarker.Children.Add(Model(Sphere(.011),Glow(0xffe0a4),true));
                world.Children.Add(guide);world.Children.Add(flown);world.Children.Add(node);
                world.Children.Add(periapsis);world.Children.Add(apoapsis);
                world.Children.Add(moving);
                renderedTarget=null;renderedCurrent=null;vehicle=null;tracesVisible=true;lastTrace=-100;
            }
            radius=target.A*(1+target.E)/target.Body.Radius;
            if(renderedTarget!=target){guide.Geometry=Trace(target,.0038*Math.Max(1,Math.Sqrt(radius)));renderedTarget=target;}
            double t=launch!=null?launch.Time:flight==null?0:flight.Time;
            Orbit active=launch!=null?Orbit.FromState(launch.Mission.Body,launch.Position,launch.Velocity,t):flight==null?null:flight.Current;
            if(renderedCurrent!=active && (active==null || renderedCurrent==null || t-lastTrace>12 || Math.Abs(active.E-renderedE)>.005)){
                flown.Geometry=Trace(active!=null&&active.Ap>=0&&active.A*(1+active.E)/body.Radius<60?active:null,.0030*Math.Max(1,Math.Sqrt(radius)));
                renderedCurrent=active;lastTrace=t;renderedE=active==null?0:active.E;
            }
            chase=follow && flight!=null;
            if(tracesVisible==chase) {
                tracesVisible=!chase;
                foreach(var item in new Model3D[]{globe,guide,flown,node,periapsis,apoapsis}) {
                    if(tracesVisible)world.Children.Insert(world.Children.Count-1,item);
                    else world.Children.Remove(item);
                }
            }
            Orbit o=active??target;
            if(chase!=wasChase || vehicle==null) {
                moving.Children.Clear();
                vehicle=chase?Spacecraft(v):mapMarker;
                moving.Children.Add(vehicle);wasChase=chase;
            }
            var pos=(launch!=null?launch.Position:flight==null?o.Position(t):flight.Position)/body.Radius;
            var marker=target.Position(0)/body.Radius;
            node.Transform=new TranslateTransform3D(marker.X,marker.Y,marker.Z);
            periapsis.Transform=OrbitMarker(active,0);
            apoapsis.Transform=OrbitMarker(active,Math.PI);
            var system=launch!=null?launch.System:flight==null?null:flight.System;
            if(system!=null&&!chase){
                var light=-MissionEnvironment.SunVector(system,body);light.Normalize();primaryLight.Direction=light;
            }
            globe.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),-360*(t/body.Day%1)));
            if(chase){
                var direction=flight.BurnDirection;
                var up=pos;up.Normalize();
                var forward=flight.Velocity-Vector3D.DotProduct(flight.Velocity,up)*up;
                if(forward.LengthSquared<1e-12)forward=Vector3D.CrossProduct(new Vector3D(0,1,0),up);
                if(forward.LengthSquared<1e-12)forward=new Vector3D(0,0,1);
                forward.Normalize();
                var side=Vector3D.CrossProduct(forward,up);side.Normalize();
                Func<Vector3D,Vector3D> toLocal=d=>new Vector3D(Vector3D.DotProduct(d,forward),Vector3D.DotProduct(d,up),Vector3D.DotProduct(d,side));
                var nose=toLocal(direction);
                var axis=Vector3D.CrossProduct(new Vector3D(0,1,0),nose);
                if(axis.Length<1e-6)axis=new Vector3D(1,0,0);
                var pose=new Transform3DGroup();
                pose.Children.Add(new ScaleTransform3D(MountedPayloadScale,MountedPayloadScale,MountedPayloadScale));
                pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis,Vector3D.AngleBetween(new Vector3D(0,1,0),nose))));
                vehicle.Transform=pose;
                primaryLight.Direction=toLocal(-MissionEnvironment.SunVector(system,body));
                Aim(new Point3D(),4.5);
                SyncPlanetBackdrop(body,t,system,flight.Position,forward,up,side,1);
            }
            else {
                Backdrop.Visibility=Visibility.Collapsed;
                double size=Math.Max(1,Math.Sqrt(radius));
                var icon=new Transform3DGroup();
                icon.Children.Add(new ScaleTransform3D(size,size,size));
                icon.Children.Add(new TranslateTransform3D(pos.X,pos.Y,pos.Z));
                vehicle.Transform=icon;
                if(active!=null && active.Pe>=0)radius=Math.Min(18,Math.Max(radius,active.A*(1+active.E)/body.Radius));
                Aim(new Point3D(),radius*3.45);
            }
        }
        static Transform3D OrbitMarker(Orbit orbit,double angle){
            if(orbit==null)return new ScaleTransform3D(0,0,0);
            var position=orbit.AtAngle(angle)/orbit.Body.Radius;
            if(position.Length<1||position.Length>60)return new ScaleTransform3D(0,0,0);
            return new TranslateTransform3D(position.X,position.Y,position.Z);
        }
        double lastTrace=-100,renderedE;
        bool wasChase;
        Model3DGroup padRocket, exhaust, ground, landscape;
        int stageSeen=-1;
        bool boosterSeen,fairingSeen,releasedSeen;
        public bool PadCamera { get; set; }
        static double GroundHeight(double x,double z,bool lunar){
            double range=Math.Sqrt(x*x+z*z);
            double strength=Math.Min(1,Math.Max(0,(range-12)/25));
            double h=Math.Sin(x*.043+z*.025)*2+Math.Sin(z*.074)*1.3+Math.Cos(x*.13-z*.097)*.7;
            if(!lunar){
                double coast=Math.Sqrt((x-9)*(x-9)+(z+12)*(z+12)*.35);
                h+=5.5-Math.Max(0,coast-28)*.22;
                return -3.35+strength*(h-.0012*range*range);
            }
            for(int i=0;i<6;i++){
                double cx=64*Math.Cos(i*2.39),cz=64*Math.Sin(i*2.39),r=7+i*1.6;
                double d=Math.Sqrt((x-cx)*(x-cx)+(z-cz)*(z-cz))/r;
                if(d<1)h-=3*(1-d*d);
                else if(d<1.28)h+=(1.28-d)*4;
            }
            return -3.35+strength*(h-.0007*range*range);
        }
        static Model3DGroup Landscape(bool lunar){
            var g=new Model3DGroup();
            const int n=76;
            var points=new Point3D[n+1,n+1];var bands=new MeshGeometry3D[6];
            for(int i=0;i<bands.Length;i++)bands[i]=new MeshGeometry3D();
            int[] colors=lunar?new[]{0x514e49,0x5c5954,0x65615b,0x77736c,0x858078,0x989188}:
                new[]{0x777957,0x73794d,0x667c47,0x748b52,0x84935d,0x98a77b};
            for(int j=0;j<=n;j++)for(int i=0;i<=n;i++){
                double u=(i-n/2)/(n/2d),v=(j-n/2)/(n/2d);
                double x=14500*Math.Sign(u)*Math.Pow(Math.Abs(u),3),z=14500*Math.Sign(v)*Math.Pow(Math.Abs(v),3);
                double radius=(lunar?Body.Moon:Body.Earth).Radius/KilometersPerModelUnit;
                double curve=(x*x+z*z)/(2*radius);
                double nearby=Math.Max(0,1-Math.Sqrt(x*x+z*z)/240);
                double local=GroundHeight(x,z,lunar);
                double distant=-3.35+(lunar?20:5)*(Math.Sin(x*.0013+z*.0007)+Math.Sin(z*.0025));
                points[i,j]=new Point3D(x,nearby*local+(1-nearby)*distant-curve,z);
            }
            for(int j=0;j<n;j++)for(int i=0;i<n;i++){
                var a=points[i,j];var b=points[i+1,j];var c=points[i+1,j+1];var d=points[i,j+1];
                int band=Math.Max(0,Math.Min(5,(int)(3+Math.Sin(a.X*.016+a.Z*.006)+Math.Sin(a.Z*.028)*.8+Math.Sin(a.X*.039-a.Z*.023)*.7)));
                var mesh=bands[band];int v=mesh.Positions.Count;
                foreach(var p in new[]{a,b,c,d})mesh.Positions.Add(p);
                foreach(int k in new[]{0,3,1,1,3,2})mesh.TriangleIndices.Add(v+k);
            }
            if(!lunar){
                var water=new MeshGeometry3D();
                double radius=Body.Earth.Radius/KilometersPerModelUnit;
                for(int j=0;j<=n;j++)for(int i=0;i<=n;i++){
                    var p=points[i,j];
                    water.Positions.Add(new Point3D(p.X,-6.4-(p.X*p.X+p.Z*p.Z)/(2*radius),p.Z));
                    if(i==0||j==0)continue;
                    int b=water.Positions.Count-1,a=b-n-1;
                    foreach(var index in new[]{a-1,b-1,a,a,b-1,b})water.TriangleIndices.Add(index);
                }
                g.Children.Add(Model(water,Lit(0x326c86,true),true));
            }
            for(int i=0;i<bands.Length;i++)g.Children.Add(Model(bands[i],Lit(colors[i]),true));
            return g;
        }
        public void LaunchView(Ascent launch,Vehicle v){
            if(ground==null){
                hangar=true;Init();
                ground=new Model3DGroup();
                landscape=Landscape(launch.Mission.Body.Lunar);world.Children.Add(landscape);
                Box(ground,0,-3.34,0,11,.21,12,0x65696c);
                Lathe(ground,0x899393,new[]{-3.23,-3.15,-3.145},new[]{1.8,1.6,0.0},slices:40);
                for(int i=-5;i<=5;i++){
                    Box(ground,i,-3.225,2.15,.52,.012,.15,0xbca05c);
                    Box(ground,i,-3.225,-2.15,.52,.012,.15,0xbca05c);
                }
                double tower=Math.Max(5,RocketTop(v)+3.3);
                for(int level=0;level<tower/1.34;level++){
                    double y=level*1.34-3.14;
                    Box(ground,3.3,y,0,1.4,.12,1.18,0x586671);
                    foreach(double z in new[]{-.5,.5}) {
                        Beam(ground,new Point3D(2.68,y,z),new Point3D(2.68,y+1.34,z),.12,0x8e999d);
                        Beam(ground,new Point3D(3.92,y,z),new Point3D(3.92,y+1.34,z),.12,0x8e999d);
                        Beam(ground,new Point3D(2.68,y,z),new Point3D(3.92,y+1.34,z),.065,0x68767b);
                        Beam(ground,new Point3D(3.92,y,z),new Point3D(2.68,y+1.34,z),.065,0x68767b);
                    }
                }
                Box(ground,1.9,1.04,0,1.8,.13,.48,0xa78a51);
                world.Children.Add(ground);
                exhaust=new Model3DGroup();
                exhaust.Children.Add(Model(Ring(new[]{-5.8,-4.2,-1.6,0.0},new[]{0.0,.29,.42,.22},24,0,0),Glow(0xea8c54),true));
                exhaust.Children.Add(Model(Ring(new[]{-4.4,-2.3,-.45,0.0},new[]{0.0,.17,.18,.18},24,0,0),Glow(0xffebaf),true));
                exhaust.Children.Add(Model(Ring(new[]{-2.3,-1.4,-.3,0.0},new[]{0.0,.12,.16,.14},24,0,0),Glow(0xfff9e0),true));
                world.Children.Add(exhaust);
            }
            if(stageSeen!=launch.Stage||boosterSeen!=launch.BoostersAttached||fairingSeen!=launch.FairingAttached||releasedSeen!=launch.InOrbit||padRocket==null){
                if(padRocket!=null)world.Children.Remove(padRocket);
                int visualStage=launch.InOrbit?launch.PayloadFromStage:launch.Stage;
                padRocket=Rocket(v,visualStage,launch.BoostersAttached,launch.FairingAttached,!launch.InOrbit);
                world.Children.Add(padRocket);stageSeen=launch.Stage;boosterSeen=launch.BoostersAttached;
                fairingSeen=launch.FairingAttached;releasedSeen=launch.InOrbit;
            }
            var planet=launch.Mission.Body;
            var rotate=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,-1,0),360*(launch.Time/planet.Day%1)));
            var padUp=rotate.Transform(launch.LaunchRadial);
            var padForward=rotate.Transform(launch.LaunchForward);
            var padSide=Vector3D.CrossProduct(padForward,padUp);padSide.Normalize();
            var up=launch.Position;up.Normalize();
            var forward=padForward-Vector3D.DotProduct(padForward,up)*up;
            if(forward.LengthSquared<1e-12)forward=Vector3D.CrossProduct(up,padSide);
            forward.Normalize();
            var side=Vector3D.CrossProduct(forward,up);side.Normalize();
            Func<Vector3D,Vector3D> toLocal=d=>new Vector3D(Vector3D.DotProduct(d,forward),Vector3D.DotProduct(d,up),Vector3D.DotProduct(d,side));
            var pad=toLocal(padUp*planet.Radius-launch.Position)/KilometersPerModelUnit;
            var padLocalX=toLocal(padForward);var padLocalY=toLocal(padUp);var padLocalZ=toLocal(padSide);
            var groundPose=new MatrixTransform3D(new Matrix3D(padLocalX.X,padLocalX.Y,padLocalX.Z,0,
                padLocalY.X,padLocalY.Y,padLocalY.Z,0,padLocalZ.X,padLocalZ.Y,padLocalZ.Z,0,pad.X,pad.Y,pad.Z,1));
            ground.Transform=landscape.Transform=groundPose;
            double groundDistance=pad.Length*KilometersPerModelUnit;
            bool localTerrain=groundDistance<28&&launch.Altitude<5.0;
            if(localTerrain&&!world.Children.Contains(landscape))world.Children.Insert(3,landscape);
            if(!localTerrain)world.Children.Remove(landscape);
            if(groundDistance>4)world.Children.Remove(ground);
            else if(!world.Children.Contains(ground))world.Children.Insert(3,ground);
            var localNose=toLocal(launch.Nose);
            // At rest the commands move the model before the first integration step.
            if(!launch.HasLifted&&launch.Acceleration==0){
                double pitch=launch.Pitch*Math.PI/180,heading=launch.HeadingOffset*Math.PI/180;
                localNose=new Vector3D(Math.Cos(pitch)*Math.Cos(heading),Math.Sin(pitch),Math.Cos(pitch)*Math.Sin(heading));
            }
            var axis=Vector3D.CrossProduct(new Vector3D(0,1,0),localNose);
            if(axis.LengthSquared<1e-12)axis=new Vector3D(0,0,1);
            var attitude=new RotateTransform3D(new AxisAngleRotation3D(axis,Vector3D.AngleBetween(new Vector3D(0,1,0),localNose)));
            padRocket.Transform=attitude;
            UpdateDeployment(launch,v,attitude);
            double plume=launch.Started && launch.Countdown<=0 && !launch.InOrbit && !launch.Failed&&launch.Acceleration>.001?Math.Min(3,Math.Sqrt(launch.Acceleration)*.7):.00001;
            var plumeTransform=new Transform3DGroup();
            plumeTransform.Children.Add(new ScaleTransform3D(Math.Min(1.6,plume),plume*(1+.06*Math.Sin(launch.Time*37)),Math.Min(1.6,plume)));
            plumeTransform.Children.Add(new TranslateTransform3D(0,launch.Stage==1?-1.81:-3.23,0));
            plumeTransform.Children.Add(attitude);
            exhaust.Transform=plumeTransform;
            int stage=launch.InOrbit?launch.PayloadFromStage:launch.Stage;
            double top=RocketTop(v,stage);
            double bottom=stage==0?-3.23:-1.81;
            var target=new Point3D(0,(top+bottom)*.35,0);
            camera.FieldOfView=43;
            camera.FarPlaneDistance=localTerrain?45000:4000;
            Aim(target,Math.Max(stage==0?13:10,(top-bottom)*1.95));
            if(PadCamera&&groundDistance>=26)PadCamera=false;
            if(PadCamera){
                var cameraAtPad=pad+toLocal(padForward*18+padSide*30+padUp*2);
                camera.Position=(Point3D)cameraAtPad;
                camera.LookDirection=target-camera.Position;
                camera.UpDirection=padLocalY;
                camera.FieldOfView=Math.Max(3,43/(1+groundDistance*4));
            }
            SyncPlanetBackdrop(planet,launch.Time,launch.System,launch.Position,forward,up,side,localTerrain?Math.Min(1,launch.Altitude/.2):1);
        }
        void Aim(Point3D target,double distance,Vector3D? outward=null) {
            aimPoint=target;aimDistance=distance;aimOutward=outward;
            if(ActualHeight>0)distance*=Math.Max(1,ActualWidth/ActualHeight);
            double yaw=Yaw*Math.PI/180,pitch=Pitch*Math.PI/180;
            var offset=new Vector3D(Math.Sin(yaw)*Math.Cos(pitch),Math.Sin(pitch),Math.Cos(yaw)*Math.Cos(pitch));
            if(outward.HasValue){
                var n=outward.Value;
                var tangent=Vector3D.CrossProduct(n,new Vector3D(0,1,0));
                if(tangent.Length<1e-5)tangent=Vector3D.CrossProduct(n,new Vector3D(1,0,0));
                tangent.Normalize();
                var along=Vector3D.CrossProduct(tangent,n);
                offset=n*.68+(tangent*Math.Cos(yaw)+along*Math.Sin(yaw))*(.54+.32*Math.Sin(pitch));
            }
            camera.Position=target+offset*distance*Zoom;
            camera.LookDirection=target-camera.Position;
            camera.UpDirection=new Vector3D(0,1,0);
        }
    }
}

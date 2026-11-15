using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Scene {
        public RoverBuild MarsPayload {get;set;}
        public int MarsCruisePackage {get;set;}
        Model3DGroup marsRover,marsField,marsCapsule,marsCanopy,marsJet;
        Model3DGroup marsShield,marsHeating;
        bool surfaceShown;
        double lastRoverHeading;
        Model3DGroup[] roverWheels;
        public void MarsRoverPreview(RoverBuild design){
            hangar=true;Init();camera.NearPlaneDistance=.09;
            world.Children.Add(Hangar(-.33,5.2,true));
            var deck=new Model3DGroup();
            Lathe(deck,0x51555c,new[]{-.32,-.20,-.19},new[]{2.65,2.65,0.0},slices:64);
            Lathe(deck,0xbf9956,new[]{-.255,-.225},new[]{2.656,2.656},slices:64);
            for(int i=-3;i<=3;i++){
                double width=2*Math.Sqrt(6.76-i*i*.42);
                Box(deck,i*.65,-.186,0,.018,.008,width,0x777a76);
                Box(deck,0,-.186,i*.65,width,.008,.018,0x777a76);
            }
            world.Children.Add(deck);
            world.Children.Add(RoverGeometry(design));
            MarsWorkshopCamera();
        }
        public void MarsWorkshopCamera(){Aim(new Point3D(0,.60,0),7.0);}
        Model3DGroup MarsCruiseGeometry(){
            var vehicle=new Model3DGroup();
            double shellRadius=MarsPayload.Fitted[1]==2?.94:.82;
            double stageRadius=MarsCruisePackage==0?.67:.80;
            Lathe(vehicle,0xaaa99c,new[]{-.77,-.67,-.18,-.03},new[]{stageRadius*.65,stageRadius,stageRadius,stageRadius*.73},slices:44);
            Lathe(vehicle,0x566474,new[]{-.70,-.63,-.54},new[]{stageRadius*1.01,stageRadius*1.01,stageRadius*1.01},slices:44);
            Lathe(vehicle,0x383e46,new[]{-.98,-.91,-.75},new[]{.19,.22,.11},slices:30);
            Lathe(vehicle,0xc6c3b4,new[]{-.02,.10,.36,.62},new[]{shellRadius*.65,shellRadius,shellRadius*.75,shellRadius*.42},slices:48);
            Lathe(vehicle,0x574039,new[]{-.08,-.03,.09},new[]{shellRadius*.75,shellRadius*.88,shellRadius},slices:48);
            for(int i=0;i<4;i++){
                double angle=i*Orbit.Circle/4;
                Box(vehicle,Math.Sin(angle)*stageRadius*.88,-.26,Math.Cos(angle)*stageRadius*.88,.25,.35,.25,0xa28855);
            }
            return vehicle;
        }
        Model3DGroup RoverGeometry(RoverBuild design){
            var rover=new Model3DGroup();
            roverWheels=new Model3DGroup[6];
            double size=design.Fitted[0]==0?1:1.24;
            Box(rover,0,.43,0,1.15*size,.29,1.47*size,0x41494b);
            Box(rover,0,.60,0,1.28*size,.24,1.59*size,0x8e6c42);
            Box(rover,0,.74,0,1.32*size,.10,1.70*size,0xbdbbb0);
            Box(rover,0,.77,0,1.25*size,.06,1.62*size,0xd2c9ae);
            foreach(int side in new[]{-1,1}){
                Box(rover,side*.655*size,.59,0,.055,.21,1.49*size,0xb38b56);
                for(int i=0;i<11;i++){
                    double z=(i-5)*.128*size;
                    Box(rover,side*.684*size,.597,z,.012,.18,.017,0x816544+i%3*0x060503);
                }
                foreach(int end in new[]{-1,1}){
                    Lathe(rover,0x434a4c,new[]{.43,.64,.78},new[]{.070,.075,.067},side*.58*size,end*.73*size,slices:12);
                    Box(rover,side*.43*size,.61,end*.81*size,.31,.16,.11,0x939891);
                    Box(rover,side*.43*size,.62,end*.87*size,.19,.085,.025,0x4b5860);
                }
                Beam(rover,new Point3D(side*.67*size,.51,-.63*size),new Point3D(side*.76*size,.35,.62*size),.025,0x575256);
            }
            for(int i=-1;i<=1;i++)foreach(int side in new[]{-1,1}){
                double x=side*.90*size,z=i*.67*size;
                var wheel=new Model3DGroup();
                Lathe(wheel,0x29282b,new[]{-.13,-.10,.10,.13},new[]{.21,.255,.255,.21},slices:30);
                Lathe(wheel,0xb8b0a3,new[]{-.135,-.135,.135,.135},new[]{0d,.12,.12,0d},slices:20);
                for(int j=0;j<20;j++){
                    double a=j*Orbit.Circle/20;
                    var tread=new Model3DGroup();Box(tread,.242,0,0,.025,.23,.040,0x67594f);
                    tread.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),a*180/Math.PI));
                    wheel.Children.Add(tread);
                }
                var transform=new Transform3DGroup();
                transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),90)));
                transform.Children.Add(new TranslateTransform3D(x,.25,z));
                var mount=new Model3DGroup{Transform=transform};
                mount.Children.Add(wheel);rover.Children.Add(mount);
                roverWheels[(i+1)*2+(side==1?1:0)]=wheel;
                var knuckle=new Point3D(side*.72*size,.37,z-i*.11*size);
                Beam(rover,new Point3D(side*.57*size,.51,i*.27),knuckle,.10,0xadada3);
                Beam(rover,knuckle,new Point3D(x,.25,z),.11,0x909590);
                Beam(rover,new Point3D(side*.59*size,.43,i*.28),new Point3D(x,.25,z),.045,0x5a6060);
            }
            if(design.Fitted[4]==0){
                for(int i=0;i<6;i++)for(int j=0;j<7;j++)
                    Box(rover,(i-2.5)*.18,.81,(j-3)*.172,.169,.026,.158,0x244e78+((i+j)%3)*0x070903);
            }else{
                for(int i=0;i<9;i++)Box(rover,(i-4)*.054,.86,-.50,.038,.21,.58,0x53595c);
            }
            if(design.Instruments[0]){
                Box(rover,-.37,1.09,.48,.09,.66,.10,0xc5c2ae);
                Beam(rover,new Point3D(-.59,.78,.51),new Point3D(-.37,1.26,.48),.03,0x8f9994);
                Box(rover,-.37,1.42,.55,.39,.18,.19,0xd5d3bd);
                foreach(double x in new[]{-.46,-.28}){
                    Box(rover,x,1.43,.66,.12,.10,.05,0x717978);
                    Box(rover,x,1.43,.693,.071,.067,.025,0x172a3b);
                    Box(rover,x+.01,1.446,.707,.023,.012,.006,0x8daac1);
                }
            }
            if(design.Instruments[1]){
                Beam(rover,new Point3D(.27,.54,.63),new Point3D(.43,.93,1.0),.09,0xc3a475);
                Box(rover,.43,.94,1.04,.21,.15,.18,0x747a79);
            }
            if(design.Instruments[2]){
                Beam(rover,new Point3D(0,.48,.74),new Point3D(-.22,.54,1.28),.13,0xa2a5a6);
                Lathe(rover,0x4f4b44,new[]{.24,.47,.60},new[]{.07,.07,.17},-.22,1.26,slices:20);
            }
            if(design.Instruments[3]){
                Beam(rover,new Point3D(.46,.75,-.60),new Point3D(.46,1.40,-.60),.032,0xb7c2bd);
                Box(rover,.46,1.40,-.60,.15,.13,.13,0xbd8b50);
            }
            double dish=design.Fitted[5]==0?.18:.32;
            Lathe(rover,0xd5d0b8,new[]{.79,.94,1.01,1.09},new[]{.035,dish*.26,dish*.75,dish},.40,-.47,slices:28);
            Lathe(rover,0x5c6259,new[]{.96,1.17},new[]{.04,.03},.40,-.47,slices:16);
            return rover;
        }
        MarsSiteData terrainData;
        Func<Point,double> terrainElevation;
        Point terrainOrigin;
        double terrainZero;
        static readonly Body MarsGlobe=new Body("Mars","mars.jpg",MarsCruise.MarsRadius,SolarSystem.GM[5],MarsCruise.MarsRotation);
        double MarsGround(double x,double z){
            double dist=Math.Sqrt(x*x+z*z);
            double terrain=(Math.Sin(x*.067+z*.046)+Math.Sin(z*.093)-Math.Cos(x*.055-z*.026))*.8;
            terrain+=Math.Max(0,z-84)*(.084+.059*Math.Sin(x*.032));
            if(terrainElevation==null)return terrain*Math.Min(1,dist/11);
            return terrainElevation(new Point(x,z))-terrainZero+MarsSiteData.TraverseRelief(x,z)-
                (x*x+z*z)/(2000*MarsCruise.MarsRadius);
        }
        Vector3D GroundNormal(double x,double z,double spacing){
            var normal=new Vector3D((MarsGround(x-spacing,z)-MarsGround(x+spacing,z))/(2*spacing),1,
                (MarsGround(x,z-spacing)-MarsGround(x,z+spacing))/(2*spacing));
            normal.Normalize();return normal;
        }
        GeometryModel3D RegionalMarsTerrain(){
            const double localEdge=136,regionalEdge=82000;
            var outer=new List<double>{localEdge};
            double spacing=22;
            while(outer[outer.Count-1]<regionalEdge){
                outer.Add(Math.Min(regionalEdge,outer[outer.Count-1]+spacing));
                spacing=Math.Min(950,spacing*1.48);
            }
            var axes=new List<double>();
            for(int i=outer.Count-1;i>=0;i--)axes.Add(-outer[i]);
            for(double position=-128;position<136;position+=8)axes.Add(position);
            foreach(double distance in outer)axes.Add(distance);
            int n=axes.Count;
            var surface=new MeshGeometry3D();
            foreach(double z in axes)foreach(double x in axes){
                surface.Positions.Add(new Point3D(x,MarsGround(x,z),z));
                surface.Normals.Add(GroundNormal(x,z,Math.Max(Math.Abs(x),Math.Abs(z))<1000?4:180));
                double u=(terrainOrigin.X+x-terrainData.ImageLeftMeters)/terrainData.ImageWidthMeters;
                double v=(terrainData.ImageTopMeters-terrainOrigin.Y-z)/terrainData.ImageHeightMeters;
                surface.TextureCoordinates.Add(new Point(Math.Max(.002,Math.Min(.998,u)),Math.Max(.002,Math.Min(.998,v))));
            }
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++){
                if(Math.Abs(axes[x])<=localEdge&&Math.Abs(axes[x+1])<=localEdge&&
                    Math.Abs(axes[z])<=localEdge&&Math.Abs(axes[z+1])<=localEdge)continue;
                int p=z*n+x;
                foreach(int index in new[]{p,p+n,p+1,p+1,p+n,p+n+1})surface.TriangleIndices.Add(index);
            }
            Material material=Lit(0xbd8666);
            if(terrainData.ImagePath!=null)material=new DiffuseMaterial(new ImageBrush(MarsRegionalAlbedo(terrainData.ImagePath)));
            return Model(surface,material,true);
        }
        static BitmapSource MarsRegionalAlbedo(string path){
            var image=new FormatConvertedBitmap(new BitmapImage(new Uri(path)),PixelFormats.Bgra32,null,0);
            int stride=image.PixelWidth*4;
            var pixels=new byte[stride*image.PixelHeight];
            image.CopyPixels(pixels,stride,0);
            for(int i=0;i<pixels.Length;i+=4){
                pixels[i]=(byte)(pixels[i]*.56);
                pixels[i+1]=(byte)(pixels[i+1]*.70);
                pixels[i+2]=(byte)(pixels[i+2]*.91);
            }
            var albedo=BitmapSource.Create(image.PixelWidth,image.PixelHeight,96,96,PixelFormats.Bgra32,null,pixels,stride);
            albedo.Freeze();return albedo;
        }
        Model3DGroup MarsTerrain(){
            var field=new Model3DGroup();
            if(terrainData!=null&&terrainElevation!=null)field.Children.Add(RegionalMarsTerrain());
            const int n=68;
            var surface=new MeshGeometry3D();
            for(int iz=0;iz<n;iz++)for(int ix=0;ix<n;ix++){
                double x=(ix-n/2)*4,z=(iz-n/2)*4;
                int p=surface.Positions.Count;
                foreach(var v in new[]{new Point(x,z),new Point(x+4,z),new Point(x+4,z+4),new Point(x,z+4)}){
                    surface.Positions.Add(new Point3D(v.X,MarsGround(v.X,v.Y),v.Y));
                    surface.TextureCoordinates.Add(new Point(v.X/12,v.Y/12));
                    surface.Normals.Add(GroundNormal(v.X,v.Y,.25));
                }
                foreach(int j in new[]{0,3,1,1,3,2})surface.TriangleIndices.Add(p+j);
            }
            int[] shades={0x805142,0x90604d,0xa16e55,0xaa7358,0xb68164,0xc38b6b,0xcd9777};
            field.Children.Add(Model(surface,new DiffuseMaterial(MarsSoil()),true));
            var rand=new Random(24485);
            for(int i=0;i<235;i++){
                double x=rand.NextDouble()*252-126,z=rand.NextDouble()*248-96;
                if(Math.Abs(x)<3&&Math.Abs(z)<4)continue;
                double r=.12+Math.Pow(rand.NextDouble(),3)*1.08,y=MarsGround(x,z);
                Lathe(field,shades[rand.Next(0,5)],new[]{y-.16,y+r*.14,y+r*.62,y+r*.72},new[]{r*.75,r,r*.44,0},x,z,5,false);
            }
            foreach(var rock in MarsFlight.FieldBoulders){
                double y=MarsGround(rock.X,rock.Y);
                Lathe(field,0x715249,new[]{y-.24,y+.23,y+.73,y+.9},new[]{.80,.93,.57,.24},rock.X,rock.Y,7,false);
                Lathe(field,0x8b6451,new[]{y-.13,y+.18,y+.39},new[]{.39,.52,.22},rock.X+.65,rock.Y-.30,6,false);
            }
            for(int i=0;i<MarsFlight.Sites.Length;i++){
                var p=MarsFlight.Sites[i].Point;
                double y=MarsGround(p.X,p.Y);
                for(int j=0;j<3;j++){
                    double x=p.X+(j-1)*1.2;
                    Lathe(field,shades[(i+2)%7],new[]{y-.25,y+.12,y+.70+j*.24,y+1.1+j*.19},new[]{.5,.98,.75,.15},x,p.Y,7,false);
                }
                Beam(field,new Point3D(p.X+1.7,y,p.Y),new Point3D(p.X+1.7,y+2.3,p.Y),.048,0x676665);
                Label(field,(i+1).ToString(),p.X+1.7,y+2.25,p.Y,.7,.42,0x0f2227,0xa2cec3);
            }
            return field;
        }
        static ImageBrush MarsSoil(){
            const int n=256;
            var texels=new byte[n*n*4];var rand=new Random(742);
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                double u=x*Orbit.Circle/n,v=y*Orbit.Circle/n;
                double noise=4*Math.Sin(2*u+v)+5*Math.Sin(5*u-3*v)+4*Math.Cos(9*u+11*v)+(rand.NextDouble()-.52)*22;
                if(rand.NextDouble()<.026)noise-=19;
                int index=4*(y*n+x);
                texels[index]=(byte)(73+noise*.8);texels[index+1]=(byte)(105+noise*.84);texels[index+2]=(byte)(156+noise);texels[index+3]=255;
            }
            var image=BitmapSource.Create(n,n,96,96,PixelFormats.Bgra32,null,texels,n*4);
            image.Freeze();
            return new ImageBrush(image){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1,1)};
        }
        void BuildMarsFlight(RoverBuild design){
            hangar=false;Init();camera.NearPlaneDistance=.04;camera.FarPlaneDistance=205000;
            world.Children.RemoveAt(0);
            world.Children.Insert(0,new AmbientLight(Rgb(0x655451)));
            world.Children.Add(new DirectionalLight(Rgb(0x927684),new Vector3D(.4,-.58,.5)));
            marsField=MarsTerrain();world.Children.Add(marsField);
            marsCapsule=new Model3DGroup();
            Lathe(marsCapsule,0x8d9b9a,new[]{-.45,-.32,.34,.64,.69},new[]{1,1,.69,.26,0},slices:48);
            marsShield=new Model3DGroup();
            if(design.Fitted[1]>0)Lathe(marsShield,0x422c2a,new[]{-.51,-.47,-.41},new[]{0,1.06,1.04},slices:48);
            marsHeating=new Model3DGroup();
            var hot=new MaterialGroup();
            hot.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(135,255,126,58))));
            marsHeating.Children.Add(Model(Ring(new[]{-1.25,-.78,-.42,.15,1.5},new[]{0.0,.83,1.14,1.16,.40},44,0,0),hot,true));
            world.Children.Add(marsCapsule);
            marsCanopy=new Model3DGroup();
            double radius=design.Fitted[2]==2?2.65:1.93;
            for(int j=0;j<12;j++){
                var mesh=new MeshGeometry3D();
                for(int k=0;k<=6;k++){
                    double a=(j*6+k)*Orbit.Circle/72;
                    foreach(var p in new[]{new Point3D(Math.Cos(a)*radius,4.3,Math.Sin(a)*radius),new Point3D(Math.Cos(a)*radius*.74,5.6,Math.Sin(a)*radius*.74),new Point3D(0,6.0,0)})mesh.Positions.Add(p);
                    if(k==0)continue;
                    int n=k*3;
                    foreach(int i in new[]{n-3,n-2,n,n-2,n+1,n,n-2,n-1,n+1})mesh.TriangleIndices.Add(i);
                }
                marsCanopy.Children.Add(Model(mesh,Lit(j%3==0?0xcb5b39:0xe9dcca),true));
                double b=j*Orbit.Circle/12;
                Beam(marsCanopy,new Point3D(Math.Cos(b)*radius,4.3,Math.Sin(b)*radius),new Point3D(0,.55,0),.012,0xc5ae94);
            }
            marsRover=RoverGeometry(design);
            marsJet=new Model3DGroup();
            foreach(int side in new[]{-1,1})
                marsJet.Children.Add(Model(Ring(new[]{-2.15,-1.35,.12},new[]{0,.16,.10},18,side*.62,0),Glow(0xffb376),true));
        }
        static void InWorld(Model3DGroup world,Model3D item,bool present){
            if(present&&!world.Children.Contains(item))world.Children.Add(item);
            if(!present)world.Children.Remove(item);
        }
        public void MarsView(MarsFlight flight,MarsSiteData site=null,Func<Point,double> elevation=null,
            MarsCruise arrival=null,Point landing=new Point()){
            if(marsField==null){
                terrainData=site;terrainElevation=elevation;
                terrainZero=elevation==null?0:elevation(new Point());
                terrainOrigin=landing;
                if(arrival!=null){Yaw=155;Pitch=15;}
                BuildMarsFlight(flight.Build);
            }
            if(flight.Landed&&!surfaceShown){
                if(terrainElevation!=null){
                    terrainZero=terrainElevation(new Point());
                    terrainOrigin=landing;
                    world.Children.Remove(marsField);
                    marsField=MarsTerrain();world.Children.Add(marsField);
                }
                surfaceShown=true;Yaw=190+flight.Heading*180/Math.PI;Pitch=6.5;Zoom=1;lastRoverHeading=flight.Heading;
            }
            InWorld(world,marsRover,flight.Engine||flight.Landed);
            InWorld(world,marsCapsule,!flight.Engine&&!flight.Landed);
            InWorld(world,marsCanopy,flight.Chute);
            InWorld(world,marsJet,flight.Engine&&flight.Throttle>0&&flight.Fuel>0&&!flight.Failed);
            double shieldAge=flight.Timer-flight.ShieldAt;
            InWorld(world,marsShield,flight.Shield||(flight.ShieldAt>=0&&shieldAge<5&&!flight.Landed));
            InWorld(world,marsHeating,flight.HeatFlux>23000&&flight.Mach>5&&!flight.Failed&&!flight.Landed);
            if(flight.Landed){
                Backdrop.Visibility=Visibility.Collapsed;
                marsField.Transform=Transform3D.Identity;
                double x=flight.Position.X,z=flight.Position.Y;
                var pose=new Transform3DGroup();
                var normal=GroundNormal(x,z,.75);
                var axis=Vector3D.CrossProduct(new Vector3D(0,1,0),normal);
                double lean=Math.Atan2(axis.Length,normal.Y)*180/Math.PI;
                pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),flight.Heading*180/Math.PI)));
                if(lean>.01)pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis,lean)));
                pose.Children.Add(new TranslateTransform3D(x,MarsGround(x,z),z));
                marsRover.Transform=pose;
                double travel=flight.Distance/.255*180/Math.PI;
                foreach(var wheel in roverWheels)
                    wheel.Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,1,0),travel));
                if(!drag.HasValue)Yaw+=(flight.Heading-lastRoverHeading)*180/Math.PI;
                lastRoverHeading=flight.Heading;
                Aim(new Point3D(x,MarsGround(x,z)+.73,z),6.4);
            }else{
                if(arrival!=null){
                    double latitude,longitude;
                    arrival.GroundCoordinates(flight.Downrange,flight.Crossrange,out latitude,out longitude);
                    double x=site.EastAt(longitude)-terrainOrigin.X,z=site.NorthAt(latitude)-terrainOrigin.Y;
                    marsField.Transform=new TranslateTransform3D(-x,-flight.Height,-z);
                    InWorld(world,marsField,flight.Height<22000&&Math.Abs(x)<78000&&Math.Abs(z)<78000);
                    Aim(new Point3D(0,flight.Chute?2.5:.1,0),flight.Chute?11:6.6);
                    RenderMarsGlobe(latitude,longitude,site.Elevation+flight.Height,arrival.SurfaceSun,
                        Math.Max(0,Math.Min(1,(flight.Height-3500)/12500)));
                }else{
                    double depth=Math.Log(1+Math.Max(0,flight.Height)/8)*6;
                    marsField.Transform=new TranslateTransform3D(0,-depth,Math.Min(82,flight.Timer*.82));
                    Aim(new Point3D(0,flight.Chute?2.5:.1,0),flight.Chute?11:6.6);
                }
                marsRover.Transform=Transform3D.Identity;
                var canopyPose=new Transform3DGroup();
                double inflation=Math.Max(.04,flight.ChuteInflation);
                canopyPose.Children.Add(new ScaleTransform3D(inflation,.64+.36*inflation,inflation,0,.55,0));
                canopyPose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),Math.Sin(flight.Timer*1.8)*4)));
                marsCanopy.Transform=canopyPose;
                var capsulePose=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),flight.Chute?0:Math.Max(0,90+flight.FlightPathAngleDegrees)));
                marsCapsule.Transform=capsulePose;marsHeating.Transform=capsulePose;
                if(flight.Shield)marsShield.Transform=capsulePose;
                else if(flight.ShieldAt>=0){
                    var detached=new Transform3DGroup();
                    detached.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,1),shieldAge*49)));
                    detached.Children.Add(new TranslateTransform3D(shieldAge*.48,-shieldAge*(.55+.46*shieldAge),0));
                    marsShield.Transform=detached;
                }
                marsJet.Transform=new ScaleTransform3D(1,.25+flight.Throttle,1);
            }
        }
        void RenderMarsGlobe(double latitude,double longitude,double altitude,Vector3D sun,double opacity){
            double lat=latitude*Math.PI/180,lon=longitude*Math.PI/180;
            var up=new Vector3D(Math.Cos(lat)*Math.Cos(lon),Math.Sin(lat),Math.Cos(lat)*Math.Sin(lon));
            var east=new Vector3D(-Math.Sin(lon),0,Math.Cos(lon));
            var north=Vector3D.CrossProduct(east,up);
            SyncPlanetBackdrop(MarsGlobe,0,null,up*(MarsGlobe.Radius+altitude/1000),east,up,north,opacity,.001,sun);
        }
    }
}

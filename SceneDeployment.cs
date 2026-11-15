using System;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed partial class Scene {
        internal const double MountedPayloadScale=.44;
        readonly Model3DGroup deploymentDebris=new Model3DGroup();
        Model3DGroup[] driftingShells, driftingBoosters;
        Model3DGroup driftingCore, releasedSpacecraft;
        Model3DGroup[] releasedPanels;
        SeparationEvent shellRendered, boosterRendered, coreRendered, payloadRendered;

        static double MountY(Vehicle v,int stage){
            return CoreTop(v)+.53+v.Stack.UpperTanks*.90+(stage==1?-1.85-CoreTop(v):0);
        }

        static double RocketRadius(Vehicle v){return .44+Math.Max(0,v.Stack.Core)*.09;}

        void MountPayload(Model3DGroup stack,Vehicle v,int stage,bool fairing,bool payload){
            double y=MountY(v,stage),r=RocketRadius(v);
            var adapter=new Model3DGroup();
            Lathe(adapter,0x73818a,new[]{y-.02,y+.04,y+.12},new[]{r*.92,r*.83,r*.49});
            Lathe(adapter,0xa98e5d,new[]{y+.115,y+.14},new[]{r*.51,r*.51});
            stack.Children.Add(adapter);
            if(fairing){
                var shroud=new Model3DGroup();
                shroud.Children.Add(FairingShell(y,r,-1));
                shroud.Children.Add(FairingShell(y,r,1));
                TagPart(shroud,"payload:1");
                stack.Children.Add(shroud);
            }else if(payload){
                Model3DGroup[] panels;
                var stowed=StowedSpacecraft(v,out panels);
                stowed.Transform=MountedTransform(y,0);
                stack.Children.Add(stowed);
            }
        }

        Model3DGroup StowedSpacecraft(Vehicle v,out Model3DGroup[] panels){
            panels=null;
            if(MarsPayload!=null)return MarsCruiseGeometry();
            var probe=Spacecraft(v);
            foreach(var model in probe.Children){
                var group=model as Model3DGroup;
                if(group==null||group.Children.Count==0)continue;
                string key;
                if(partKeys.TryGetValue(group.Children[0],out key)&&key=="payload:3"){
                    panels=new[]{new Model3DGroup(),new Model3DGroup()};
                    while(group.Children.Count>0){
                        var element=group.Children[0];
                        group.Children.RemoveAt(0);
                        int side=element.Bounds.X+element.Bounds.SizeX/2<0?0:1;
                        panels[side].Children.Add(element);
                    }
                    foreach(var wing in panels)group.Children.Add(wing);
                    SetPanelAngle(v,panels,83);
                    break;
                }
            }
            return probe;
        }

        static void SetPanelAngle(Vehicle v,Model3DGroup[] wings,double angle){
            double hinge=.52+.12*Math.Max(0,v.Selections[1])+.12;
            for(int i=0;i<wings.Length;i++){
                int side=i==0?-1:1;
                wings[i].Transform=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),side*angle),side*hinge,.04,.02);
            }
        }

        static Transform3D MountedTransform(double mount,double lift){
            var transform=new Transform3DGroup();
            transform.Children.Add(new ScaleTransform3D(MountedPayloadScale,MountedPayloadScale,MountedPayloadScale));
            transform.Children.Add(new TranslateTransform3D(0,mount+.54+lift,0));
            return transform;
        }

        static MeshGeometry3D ShellMesh(double[] ys,double[] radii,double start,double end){
            const int segments=32;
            var mesh=new MeshGeometry3D();
            for(int row=0;row<ys.Length;row++){
                for(int col=0;col<=segments;col++){
                    double azimuth=start+(end-start)*col/segments;
                    mesh.Positions.Add(new Point3D(radii[row]*Math.Cos(azimuth),ys[row],radii[row]*Math.Sin(azimuth)));
                    if(row==0||col==0)continue;
                    int i=mesh.Positions.Count-1,above=i-segments-1;
                    foreach(int index in new[]{above-1,i-1,above,above,i-1,i})mesh.TriangleIndices.Add(index);
                }
            }
            return mesh;
        }

        static Model3DGroup FairingShell(double bottom,double radius,int side){
            var shell=new Model3DGroup();
            double center=side==1?0:Math.PI;
            double start=center-Math.PI/2+.012,end=center+Math.PI/2-.012;
            var ys=new[]{bottom,bottom+.20,bottom+.72,bottom+1.0,bottom+1.2,bottom+1.32};
            var r=new[]{radius*.91,radius*1.13,radius*1.13,radius*.87,radius*.40,.001};
            var skin=Model(ShellMesh(ys,r,start,end),Lit(0xe0e1d7,true));
            skin.BackMaterial=Lit(0xb9a17a);
            shell.Children.Add(skin);
            foreach(double edge in new[]{start,end}){
                for(int row=1;row<ys.Length;row++){
                    var a=new Point3D(r[row-1]*Math.Cos(edge),ys[row-1],r[row-1]*Math.Sin(edge));
                    var b=new Point3D(r[row]*Math.Cos(edge),ys[row],r[row]*Math.Sin(edge));
                    Beam(shell,a,b,.016,0x616d72);
                }
            }
            var belt=ShellMesh(new[]{bottom+.21,bottom+.243},new[]{radius*1.14,radius*1.14},start,end);
            shell.Children.Add(Model(belt,Lit(0x505d67),true));
            return shell;
        }

        Model3DGroup BoosterGeometry(Vehicle v,int mount,int side){
            var model=new Model3DGroup();
            double distance=RocketRadius(v)+.40;
            double x=mount==0?distance*side:0,z=mount==1?distance*side:0;
            double r=v.Stack.Booster==1?.27:.32;
            Lathe(model,0xd5d8ce,new[]{-2.82,-2.68,-.26,-.03,.36},new[]{r*.86,r,r,r*.76,0.0},x,z,40);
            EngineBell(model,x,-3.25,z,r*.76);
            for(int ring=0;ring<5;ring++){
                double y=-2.62+ring*.56;
                Lathe(model,ring==0?0xaf743e:0x79817e,new[]{y,y+.03},new[]{r*1.014,r*1.014},x,z,40);
            }
            return model;
        }

        const double FairingVisibleFor=3.3,StageVisibleFor=4.5;
        internal const double PayloadRevealDuration=2.8;

        void UpdateDeployment(Ascent launch,Vehicle v,Transform3D attitude){
            if(!world.Children.Contains(deploymentDebris))world.Children.Add(deploymentDebris);
            int currentStage=launch.InOrbit?launch.PayloadFromStage:launch.Stage;
            double elapsed=launch.DeploymentViewSeconds;
            if(launch.FairingEvent!=null){
                var e=launch.FairingEvent;
                double age=EventAge(launch,e);
                if(age<=FairingVisibleFor){
                    if(shellRendered!=e){
                        shellRendered=e;
                        double y=MountY(v,e.Stage);
                        driftingShells=new[]{FairingShell(y,RocketRadius(v),-1),FairingShell(y,RocketRadius(v),1)};
                        foreach(var shell in driftingShells)deploymentDebris.Children.Add(shell);
                    }
                    for(int i=0;i<2;i++){
                        int side=i==0?-1:1;
                        double offset=age*(.62+.30*age);
                        double turn=Math.Min(145,age*(27+11*age));
                        double baseY=MountY(v,e.Stage);
                        var turnAtHinge=new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0,0,1),-side*turn),0,baseY,0);
                        var shellMotion=new Transform3DGroup();
                        shellMotion.Children.Add(turnAtHinge);
                        shellMotion.Children.Add(new TranslateTransform3D(side*offset,MountY(v,currentStage)-baseY-.20*age*age,-.14*age));
                        shellMotion.Children.Add(SeparationAttitude(e));
                        driftingShells[i].Transform=shellMotion;
                    }
                }else if(driftingShells!=null){
                    foreach(var shell in driftingShells)deploymentDebris.Children.Remove(shell);
                    driftingShells=null;
                }
            }
            if(launch.BoosterEvent!=null){
                var e=launch.BoosterEvent;
                double age=EventAge(launch,e);
                if(age<=StageVisibleFor){
                    if(boosterRendered!=e){
                        boosterRendered=e;
                        driftingBoosters=new Model3DGroup[v.Stack.BoosterPairs*2];
                        for(int i=0;i<driftingBoosters.Length;i++){
                            var booster=BoosterGeometry(v,i/2,i%2==0?-1:1);
                            deploymentDebris.Children.Add(booster);driftingBoosters[i]=booster;
                        }
                    }
                    for(int i=0;i<driftingBoosters.Length;i++){
                        int side=i%2==0?-1:1;
                        var axis=i<2?new Vector3D(0,0,-side):new Vector3D(side,0,0);
                        double offset=side*age*(.16+age*.15);
                        var move=new Transform3DGroup();
                        move.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis,age*12),0,-1.6,0));
                        move.Children.Add(new TranslateTransform3D(i<2?offset:0,MountY(v,currentStage)-MountY(v,0)-age*(.30+.64*age),i>=2?offset:0));
                        move.Children.Add(SeparationAttitude(e));
                        driftingBoosters[i].Transform=move;
                    }
                }else if(driftingBoosters!=null){
                    foreach(var booster in driftingBoosters)deploymentDebris.Children.Remove(booster);
                    driftingBoosters=null;
                }
            }
            if(launch.CoreEvent!=null){
                var e=launch.CoreEvent;
                double age=EventAge(launch,e);
                if(age<=StageVisibleFor){
                    if(coreRendered!=e){
                        coreRendered=e;driftingCore=Rocket(v,0,false,false,false,true);
                        deploymentDebris.Children.Add(driftingCore);
                    }
                    var move=new Transform3DGroup();
                    move.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1,0,.2),age*8),0,-1.8,0));
                    move.Children.Add(new TranslateTransform3D(age*.09,MountY(v,currentStage)-MountY(v,0)-age*(.35+.58*age),0));
                    move.Children.Add(SeparationAttitude(e));driftingCore.Transform=move;
                }else if(driftingCore!=null){
                    deploymentDebris.Children.Remove(driftingCore);driftingCore=null;
                }
            }
            if(launch.PayloadEvent!=null&&v.Wet>0){
                if(payloadRendered!=launch.PayloadEvent){
                    payloadRendered=launch.PayloadEvent;
                    releasedSpacecraft=StowedSpacecraft(v,out releasedPanels);
                    deploymentDebris.Children.Add(releasedSpacecraft);
                }
                var move=new Transform3DGroup();
                move.Children.Add(MountedTransform(MountY(v,currentStage),elapsed*(.42+.13*elapsed)));
                move.Children.Add(attitude);releasedSpacecraft.Transform=move;
                if(releasedPanels!=null){
                    double fraction=Math.Max(0,Math.Min(1,(elapsed-1.0)/1.4));
                    SetPanelAngle(v,releasedPanels,83*(1-fraction*fraction*(3-2*fraction)));
                }
            }
        }

        static double EventAge(Ascent launch,SeparationEvent separation){
            return launch.InOrbit
                ?launch.PayloadEvent.Time-separation.Time+launch.DeploymentViewSeconds
                :launch.Time-separation.Time;
        }

        static Transform3D SeparationAttitude(SeparationEvent e){
            double pitch=e.Pitch*Math.PI/180,heading=e.Heading*Math.PI/180;
            var normal=new Vector3D(Math.Cos(pitch)*Math.Cos(heading),Math.Sin(pitch),Math.Cos(pitch)*Math.Sin(heading));
            var axis=Vector3D.CrossProduct(new Vector3D(0,1,0),normal);
            if(axis.LengthSquared<1e-12)axis=new Vector3D(0,0,1);
            return new RotateTransform3D(new AxisAngleRotation3D(axis,90-e.Pitch));
        }
    }
}

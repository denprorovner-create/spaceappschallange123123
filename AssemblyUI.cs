using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Perilune {
    sealed partial class Game {
        readonly Stack<Vehicle> buildUndo=new Stack<Vehicle>();
        string selectedPart="", designName="Untitled vehicle";
        TextBlock partName,partDetail,partDelta,buildSummary;
        StackPanel installedList;
        int rocketCategory;
        const string PartFormat="Perilune.AssemblyPart";
        StackDesign StarterStack(){return LunarTransfer.StarterStack(mission);}
        sealed class AssemblyPart {public string Socket,Name,Detail;public Action<Vehicle> Install;}
        void RememberDesign(){buildUndo.Push(vehicle.Copy());}
        void ChangeDesign(Action<Vehicle> apply){RememberDesign();apply(vehicle);Build();}
        void UndoDesign(){if(buildUndo.Count>0){vehicle=buildUndo.Pop();Build();}}
        void BuildKeys(KeyEventArgs e){
            if(e.OriginalSource is TextBox || e.OriginalSource is ComboBox)return;
            if(e.Key==Key.Delete || e.Key==Key.Back){DetachSelected();e.Handled=true;}
            if(e.Key==Key.Z && (Keyboard.Modifiers&ModifierKeys.Control)!=0){UndoDesign();e.Handled=true;}
            if(e.Key==Key.Escape){selectedPart="";scene.SelectedPart="";ShowSelected();}
        }
        void PartCard(StackPanel list,string key,string name,string detail,Action<Vehicle> install){
            var part=new AssemblyPart{Socket=key,Name=name,Detail=detail,Install=install};
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(52)});row.ColumnDefinitions.Add(new ColumnDefinition());
            Column(row,new PartGlyph(key){Width=46,Height=65},0);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
            Add(text,T(name,13,White,true),4);Add(text,T(detail,10,Muted));Column(row,text,1);
            var card=Frame(row,8);card.Cursor=Cursors.Hand;card.ToolTip="Drag into the assembly bay. Double-click to attach.";
            Point start=new Point();bool ready=false;
            card.MouseLeftButtonDown+=(s,e)=>{
                if(e.ClickCount==2){ChangeDesign(part.Install);e.Handled=true;return;}
                start=e.GetPosition(card);ready=true;card.CaptureMouse();PreviewPart(part);e.Handled=true;
            };
            card.MouseLeftButtonUp+=(s,e)=>{ready=false;card.ReleaseMouseCapture();};
            card.MouseMove+=(s,e)=>{
                if(!ready||e.LeftButton!=MouseButtonState.Pressed||(e.GetPosition(card)-start).Length<5)return;
                ready=false;card.ReleaseMouseCapture();PreviewPart(part);
                DragDrop.DoDragDrop(card,new DataObject(PartFormat,part),DragDropEffects.Copy);
            };
            card.MouseEnter+=(s,e)=>{card.BorderBrush=Cyan;PreviewPart(part);};
            card.MouseLeave+=(s,e)=>{card.BorderBrush=Line;ShowSelected();};
            var menu=new ContextMenu();var attach=new MenuItem{Header="Attach "+name};attach.Click+=(s,e)=>ChangeDesign(part.Install);menu.Items.Add(attach);card.ContextMenu=menu;
            Add(list,card,7);
        }
        void PreviewPart(AssemblyPart part){
            if(partName==null)return;var candidate=vehicle.Copy();part.Install(candidate);
            partName.Text=part.Name;partDetail.Text=part.Detail;
            double mass=candidate.Wet-vehicle.Wet,cost=candidate.Cost-vehicle.Cost;
            bool marsRocket=screen=="mars-rocket";
            double payloadMass=marsRocket?HellasMass:vehicle.Wet;
            if(part.Socket.IndexOf("payload")!=0)mass=candidate.Stack.LaunchMass(payloadMass)-vehicle.Stack.LaunchMass(payloadMass);
            double committed=marsRocket?HellasCost+cost:candidate.Cost,cap=marsRocket?HellasBudget:mission.Budget;
            double balance=cap-committed;
            partDelta.Foreground=balance<0?Red:Cyan;
            string costChange=(cost>0?"+":cost<0?"−":"")+Money(cost);
            partDelta.Text=string.Format(
                "IF ATTACHED  {0:+#,0;-#,0;0} kg · {1}\n{2} of {3} committed\n{4}\nLiftoff TWR  {5:0.00} → {6:0.00}\nPeak demand  {7:0} → {8:0} W",
                mass,costChange,Money(committed),Money(cap),BudgetBalance(balance),
                vehicle.Stack.PadTwr(payloadMass,Body.Earth),candidate.Stack.PadTwr(marsRocket?payloadMass:candidate.Wet,Body.Earth),PeakPower(vehicle),PeakPower(candidate));
            if(marsRocket)partDelta.Text=partDelta.Text.Substring(0,partDelta.Text.LastIndexOf('\n'));
        }
        static double PeakPower(Vehicle v){return v[1].Power+v[4].Power+v.InstrumentPower;}
        void AttachDrop(Grid view){
            view.AllowDrop=true;
            view.DragOver+=(s,e)=>{
                var part=e.Data.GetData(PartFormat) as AssemblyPart;
                e.Effects=part==null?DragDropEffects.None:DragDropEffects.Copy;
                if(part!=null&&scene.SelectedPart!=part.Socket){
                    scene.SelectedPart=part.Socket;
                    PreviewPart(part);
                }
                e.Handled=true;
            };
            view.DragLeave+=(s,e)=>{scene.SelectedPart=selectedPart;ShowSelected();};
            view.Drop+=(s,e)=>{
                var p=e.Data.GetData(PartFormat) as AssemblyPart;if(p==null)return;
                selectedPart=p.Socket;RememberDesign();p.Install(vehicle);e.Effects=DragDropEffects.Copy;e.Handled=true;Dispatcher.BeginInvoke(new Action(Build));
            };
        }
        void ShowSelected(){
            if(partName==null)return;
            partName.Text=string.IsNullOrEmpty(selectedPart)?"Select a component":SelectedName();
            partDetail.Text=string.IsNullOrEmpty(selectedPart)?"Click a fitted part to inspect it. Drag a catalog part into the bay to attach it.":"Delete detaches this component. Ctrl+Z restores your last change.";partDelta.Text="";
        }
        string SelectedName(){
            switch(selectedPart){
                case "core":return vehicle.Stack.Core>=0?vehicle.Stack.Main.Name+" core engine":"Core engine mount";
                case "upper":return vehicle.Stack.Engine>=0?vehicle.Stack.Vacuum.Name+" upper engine":"Upper engine mount";
                case "core fuel":return "Core propellant · "+vehicle.Stack.CoreTanks+" tanks";
                case "upper fuel":return "Upper propellant · "+vehicle.Stack.UpperTanks+" tanks";
                case "booster":return vehicle.Stack.Booster>0?vehicle.Stack.Assist.Name:"Radial booster mounts";
            }
            var fields=selectedPart.Split(':');int n;
            if(fields.Length>=2&&int.TryParse(fields[1],out n)&&n>=1&&n<=6){
                if(n==5&&fields.Length==3&&int.TryParse(fields[2],out n))return Vehicle.Catalog[5][n].Name;return vehicle[n].Name;
            }return "Component";
        }
        void DetachSelected(){
            var key=selectedPart;if(string.IsNullOrEmpty(key))return;
            ChangeDesign(v=>{
                switch(key){
                    case "core":v.Stack.Core=-1;break;
                    case "upper":v.Stack.Engine=-1;break;
                    case "core fuel":v.Stack.CoreTanks=Math.Max(0,v.Stack.CoreTanks-1);break;
                    case "upper fuel":v.Stack.UpperTanks=Math.Max(0,v.Stack.UpperTanks-1);break;
                    case "booster":if(v.Stack.BoosterPairs>1)v.Stack.BoosterPairs--;else v.Stack.Booster=0;break;
                    default:
                        var fields=key.Split(':');int n;
                        if(fields.Length>=2&&int.TryParse(fields[1],out n)&&n>=1&&n<=6){
                            if(n==5&&fields.Length==3){int ins=int.Parse(fields[2]);if(v.Selections[5]==ins)v.Selections[5]=-1;v.ExtraInstruments[ins]=false;}
                            else {v.Selections[n]=-1;if(n==5)Array.Clear(v.ExtraInstruments,0,5);}
                        }break;
                }
            });
        }
        void FittedRow(string key,string name,string info){
            var p=new DockPanel();var del=Btn("×",()=>{selectedPart=key;DetachSelected();});del.ToolTip="Detach "+name;del.MinWidth=31;DockPanel.SetDock(del,Dock.Right);p.Children.Add(del);
            var b=Btn(name,()=>{selectedPart=key;scene.SelectedPart=key;ShowSelected();});b.ToolTip=info;b.FontSize=11;p.Children.Add(b);Add(installedList,p,3);
        }
        void RenderInstalled(){
            var d=vehicle.Stack;
            if(stack){
                if(d.Engine>=0)FittedRow("upper",d.Vacuum.Name,"Upper engine");
                if(d.UpperTanks>0)FittedRow("upper fuel",d.UpperTanks+" upper tanks","Upper stage propellant");
                if(d.CoreTanks>0)FittedRow("core fuel",d.CoreTanks+" core tanks","Core propellant");
                if(d.Core>=0)FittedRow("core",d.Main.Name,"Core engine");
                if(d.Booster>0)FittedRow("booster",d.BoosterPairs*2+" radial boosters",d.Assist.Name);
            }else{
                for(int i=1;i<=6;i++)if(i!=5&&vehicle.Has(i))FittedRow("payload:"+i,vehicle[i].Name,Vehicle.Slots[i]);
                foreach(int i in vehicle.InstalledInstruments)FittedRow("payload:5:"+i,Vehicle.Catalog[5][i].Name+(i==mission.Instrument?" ★":""),"Science instrument");
            }
        }
        void RenderParts(StackPanel list){
            bool marsRocket=screen=="mars-rocket";
            var headers=new UniformGrid{Columns=2};
            headers.Children.Add(Btn("Rocket",()=>{stack=true;selectedPart="";Build();},stack));
            headers.Children.Add(marsRocket?Btn("Rover",MarsWorkshop):Btn("Spacecraft",()=>{stack=false;slot=Math.Max(1,slot);selectedPart="";Build();},!stack));Add(list,headers,12);
            if(stack){
                var cat=Options(new[]{"Core engines","Core tanks","Upper engines","Upper tanks","Side boosters"},rocketCategory);
                cat.SelectionChanged+=(s,e)=>{if(cat.SelectedIndex>=0){rocketCategory=cat.SelectedIndex;Build();}};Add(list,cat,12);
                switch(rocketCategory){
                    case 0: PropulsionCards(list,"core",StackDesign.Cores,0,(v,i)=>v.Stack.Core=i);break;
                    case 1: TankCard(list,false);break;
                    case 2: PropulsionCards(list,"upper",StackDesign.Upper,0,(v,i)=>v.Stack.Engine=i);break;
                    case 3: TankCard(list,true);break;
                    case 4: PropulsionCards(list,"booster",StackDesign.Boosters,1,(v,i)=>{v.Stack.BoosterPairs=v.Stack.Booster==i?Math.Min(2,v.Stack.BoosterPairs+1):1;v.Stack.Booster=i;});break;
                }
                var load=Range(list,"PROPELLANT LOAD",10,100,vehicle.Stack.Load*100,5,"%");
                load.PreviewMouseLeftButtonDown+=(s,e)=>RememberDesign();load.ValueChanged+=(s,e)=>{vehicle.Stack.Load=load.Value/100;UpdateBuildSummary();};load.PreviewMouseLeftButtonUp+=(s,e)=>Build();
            }else{
                var family=Options(Vehicle.Slots.Skip(1).ToArray(),slot-1);family.SelectionChanged+=(s,e)=>{if(family.SelectedIndex>=0){slot=family.SelectedIndex+1;Build();}};Add(list,family,12);
                int familySlot=slot;
                for(int i=0;i<Vehicle.Catalog[slot].Length;i++){
                    int index=i;var prop=Vehicle.Catalog[slot][i];string detail=string.Format("{0:0} kg · {1}\n{2}",prop.Mass,Money(prop.Cost),prop.Detail);
                    if(slot==5&&vehicle.HasInstrument(i))detail="INSTALLED · "+detail;
                    PartCard(list,slot==5?"payload:5:"+i:"payload:"+slot,prop.Name,detail,v=>{
                        if(familySlot==5){if(v.Selections[5]<0)v.Selections[5]=index;else if(v.Selections[5]!=index)v.ExtraInstruments[index]=true;}else v.Selections[familySlot]=index;
                    });
                }
            }
            Add(list,Mono("FITTED COMPONENTS",Cyan),7);installedList=new StackPanel();Add(list,installedList,12);RenderInstalled();
            Add(list,Btn("Undo   Ctrl+Z",UndoDesign),5);
            Add(list,Btn("Clear "+(stack?"rocket":"spacecraft"),()=>ChangeDesign(v=>{
                if(stack)v.Stack=new StackDesign{Core=-1,Engine=-1,CoreTanks=0,UpperTanks=0};else {for(int i=1;i<=6;i++)v.Selections[i]=-1;Array.Clear(v.ExtraInstruments,0,5);}
            })),5);
            if(marsRocket)Add(list,Btn("Restore stock rocket",()=>ChangeDesign(v=>v.Stack=HellasStarter())),5);
            else Add(list,Btn("Starter vehicle",()=>ChangeDesign(v=>{var starter=new Vehicle();Array.Copy(starter.Selections,v.Selections,7);Array.Clear(v.ExtraInstruments,0,5);v.Stack=StarterStack();v.Selections[1]=0;v.Selections[5]=mission.Instrument;v.Selections[3]=mission.Instrument==2?2:1;if(mission.Body.Lunar)v.Selections[2]=3;})),5);
        }
        void TankCard(StackPanel list,bool upper){
            int count=upper?vehicle.Stack.UpperTanks:vehicle.Stack.CoreTanks;
            double price=upper?StackDesign.UpperTankCost:StackDesign.CoreTankCost;
            string detail=Money(price)+" per tank · "+count+"/6 fitted";
            PartCard(list,upper?"upper fuel":"core fuel",upper?"Upper propellant tank":"Core propellant tank",detail,v=>{
                if(upper)v.Stack.UpperTanks=Math.Min(6,v.Stack.UpperTanks+1);
                else v.Stack.CoreTanks=Math.Min(6,v.Stack.CoreTanks+1);
            });
        }
        void PropulsionCards(StackPanel list,string socket,Propulsion[] parts,int start,Action<Vehicle,int> apply){
            for(int i=start;i<parts.Length;i++){int index=i;var p=parts[i];PartCard(list,socket,p.Name,string.Format("{0} · {1}{2}\n{3:N0} kN · {4:0} s Isp",p.Info,Money(p.Cost),socket=="booster"?" / pair":"",p.Thrust/1000,p.Isp),v=>apply(v,index));}
        }
        void UpdateBuildSummary(){
            if(screen=="mars-rocket"){
                if(refreshHellasLauncher!=null)refreshHellasLauncher();
                return;
            }
            if(buildSummary==null)return;var a=MissionAnalysis.Analyze(mission,vehicle);
            buildSummary.Text=string.Format("{0:N0} kg at liftoff\nTWR {1:0.00}   ·   Δv {2:0.00} km/s\n\nPOWER\n{3:0} W generated / {4:0} W peak\n{5:0} min battery at peak load\n\nDATA\n{6:0.0} GB recorder\n{7:0.00} GB/h science\n{8:0.00} GB/h radio",a.MassKg,a.PadTwr,a.AscentDeltaV,a.GenerationW,a.PeakW,a.BatteryMinutes,vehicle[1].A,vehicle.InstrumentRate,vehicle[4].A);
        }
    }
    sealed class PartGlyph : FrameworkElement {
        readonly string key;public PartGlyph(string key){this.key=key;IsHitTestVisible=false;}
        protected override void OnRender(DrawingContext c){
            base.OnRender(c);var white=new SolidColorBrush(Color.FromRgb(207,216,223));var metal=new SolidColorBrush(Color.FromRgb(82,102,116));var gold=new SolidColorBrush(Color.FromRgb(205,168,92));var blue=new SolidColorBrush(Color.FromRgb(48,119,167));var pen=new Pen(metal,1);double x=ActualWidth/2,y=ActualHeight/2;
            if(key=="booster"){foreach(int s in new[]{-1,1}){c.DrawRoundedRectangle(white,pen,new Rect(x+s*11-6,y-20,12,39),4,4);c.DrawRectangle(gold,null,new Rect(x+s*11-6,y+8,12,4));}return;}
            if(key.Contains("fuel")){c.DrawRoundedRectangle(white,pen,new Rect(x-12,y-24,24,48),8,8);foreach(int s in new[]{-1,1})c.DrawRectangle(metal,null,new Rect(x-12,y+s*15,24,3));return;}
            if(key=="core"||key=="upper"){
                c.DrawRectangle(metal,pen,new Rect(x-8,y-22,16,18));var g=new StreamGeometry();using(var p=g.Open()){p.BeginFigure(new Point(x-6,y-4),true,true);p.LineTo(new Point(x-16,y+20),true,false);p.LineTo(new Point(x+16,y+20),true,false);p.LineTo(new Point(x+6,y-4),true,false);}c.DrawGeometry(white,pen,g);c.DrawEllipse(metal,null,new Point(x,y+20),16,4);return;
            }
            if(key=="payload:3"){c.DrawRectangle(gold,pen,new Rect(x-5,y-9,10,18));foreach(int s in new[]{-1,1}){c.DrawRectangle(blue,pen,new Rect(x+s*15-8,y-18,16,36));for(int i=-1;i<=1;i++)c.DrawLine(new Pen(white,.5),new Point(x+s*15-8,y+i*9),new Point(x+s*15+8,y+i*9));}return;}
            if(key=="payload:4"||key=="payload:6"){c.DrawLine(new Pen(metal,4),new Point(x,y+24),new Point(x,y-3));c.DrawEllipse(white,pen,new Point(x,y-9),18,10);c.DrawLine(new Pen(gold,2),new Point(x,y-9),new Point(x+9,y-27));return;}
            c.DrawRoundedRectangle(key.Contains(":5")?metal:gold,pen,new Rect(x-16,y-17,32,34),3,3);c.DrawEllipse(white,pen,new Point(x,y),10,10);c.DrawEllipse(blue,null,new Point(x,y),7,7);
        }
    }
}

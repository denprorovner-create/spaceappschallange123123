using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Perilune {
    // A flight instrument, not an attitude controller: the amber mark is the nose,
    // and the green mark is the actual velocity direction relative to the horizon.
    sealed class Navball : FrameworkElement {
        public double Pitch, VelocityPitch=double.NaN, Throttle;
        public string Mode="SURFACE";
        static Brush Color(int rgb) { return new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb)); }
        static readonly Brush Ink=Color(0x0c1219), Rim=Color(0x34424e), Silver=Color(0x9aa9b2), White=Color(0xe9f0ef), Gold=Color(0xf1ca73), Green=Color(0xb7df76);
        public Navball(){Width=210;Height=218;IsHitTestVisible=false;}
        void Text(DrawingContext dc,string value,double size,Brush brush,double x,double y,bool center=true){
            var t=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("Consolas"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(t,new Point(center?x-t.Width/2:x,y));
        }
        protected override void OnRender(DrawingContext dc){
            if(ActualWidth<1||ActualHeight<1)return;
            dc.PushTransform(new ScaleTransform(ActualWidth/210,ActualHeight/218));
            Point c=new Point(105,110);const double r=82;
            dc.DrawEllipse(Ink,new Pen(Rim,2),c,97,97);
            dc.DrawEllipse(Rim,new Pen(Silver,.8),c,90,90);
            dc.DrawEllipse(Ink,null,c,86,86);
            dc.PushClip(new EllipseGeometry(c,r,r));
            double horizon=c.Y+Math.Max(-1.6,Math.Min(1.6,Pitch/60))*r;
            dc.DrawRectangle(new LinearGradientBrush(System.Windows.Media.Color.FromRgb(19,63,104),System.Windows.Media.Color.FromRgb(75,143,176),90),null,new Rect(0,0,210,218));
            dc.DrawRectangle(new LinearGradientBrush(System.Windows.Media.Color.FromRgb(140,101,59),System.Windows.Media.Color.FromRgb(69,47,36),90),null,new Rect(-100,horizon,410,500));
            dc.DrawLine(new Pen(White,1.7),new Point(-30,horizon),new Point(240,horizon));
            for(int angle=-90;angle<=90;angle+=10){
                if(angle==0)continue;
                double y=c.Y+(Pitch-angle)*r/60;
                if(y<c.Y-r-10||y>c.Y+r+10)continue;
                double width=angle%30==0?42:27;
                var p=new Pen(White,angle%30==0?1.1:.7);
                if(angle<0)p.DashStyle=DashStyles.Dash;
                dc.DrawLine(p,new Point(c.X-width,y),new Point(c.X-8,y));
                dc.DrawLine(p,new Point(c.X+8,y),new Point(c.X+width,y));
                if(angle%30==0){Text(dc,Math.Abs(angle).ToString(),10,White,c.X-width-12,y-6);Text(dc,Math.Abs(angle).ToString(),10,White,c.X+width+12,y-6);}
            }
            for(int x=-3;x<=3;x++)dc.DrawLine(new Pen(White,.55),new Point(c.X+x*23,c.Y-110),new Point(c.X+x*23,c.Y+110));
            if(!double.IsNaN(VelocityPitch)){
                double py=c.Y+(Pitch-VelocityPitch)*r/60;
                py=Math.Max(c.Y-r+12,Math.Min(c.Y+r-12,py));
                Point velocity=new Point(c.X,py);
                dc.DrawEllipse(null,new Pen(Ink,4),velocity,7,7);
                dc.DrawEllipse(null,new Pen(Green,2),velocity,7,7);
                dc.DrawLine(new Pen(Green,2),new Point(velocity.X-15,py),new Point(velocity.X-7,py));
                dc.DrawLine(new Pen(Green,2),new Point(velocity.X+7,py),new Point(velocity.X+15,py));
                dc.DrawLine(new Pen(Green,2),new Point(velocity.X,py-15),new Point(velocity.X,py-7));
            }
            dc.Pop();
            var pointer=new StreamGeometry();
            using(var s=pointer.Open()){
                s.BeginFigure(new Point(72,110),false,false);s.LineTo(new Point(94,110),true,false);s.LineTo(new Point(105,119),true,false);
                s.LineTo(new Point(116,110),true,false);s.LineTo(new Point(138,110),true,false);
            }
            dc.DrawGeometry(null,new Pen(Ink,6),pointer);dc.DrawGeometry(null,new Pen(Gold,3),pointer);
            for(int i=-60;i<=60;i+=15){
                double a=(i-90)*Math.PI/180;
                Point p=new Point(c.X+Math.Cos(a)*91,c.Y+Math.Sin(a)*91);
                Point q=new Point(c.X+Math.Cos(a)*(i%30==0?84:87),c.Y+Math.Sin(a)*(i%30==0?84:87));
                dc.DrawLine(new Pen(Silver,1.1),p,q);
            }
            dc.DrawRoundedRectangle(Ink,new Pen(Rim,1),new Rect(62,3,86,21),3,3);
            Text(dc,Mode,11,White,105,6);
            dc.DrawRoundedRectangle(Ink,new Pen(Rim,1),new Rect(66,188,78,23),3,3);
            Text(dc,Pitch.ToString("+0.0;−0.0;0.0")+"°",13,Gold,105,191);
            double throttle=Math.Max(0,Math.Min(1,Throttle));
            dc.DrawRoundedRectangle(Ink,new Pen(Rim,1),new Rect(2,73,7,76),2,2);
            dc.DrawRectangle(Gold,null,new Rect(3,148-throttle*74,5,throttle*74));
            Text(dc,"THR",8,Silver,8,58);
            Text(dc,(throttle*100).ToString("0"),9,Gold,9,153);
            dc.Pop();
        }
    }

    sealed partial class Game {
        static Border HudFrame(UIElement child,double pad=12){
            return new Border{Child=child,Padding=new Thickness(pad),CornerRadius=new CornerRadius(3),
                Background=new SolidColorBrush(System.Windows.Media.Color.FromArgb(229,17,24,31)),
                BorderBrush=new SolidColorBrush(System.Windows.Media.Color.FromArgb(160,100,120,132)),BorderThickness=new Thickness(1)};
        }
    }
}

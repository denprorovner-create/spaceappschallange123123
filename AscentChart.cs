using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Perilune {
    sealed class AscentChart : FrameworkElement {
        readonly Ascent rocket;
        static readonly Brush Axis=new SolidColorBrush(Color.FromRgb(62,76,73));
        static readonly Brush Caption=new SolidColorBrush(Color.FromRgb(172,186,180));
        static readonly Brush Core=new SolidColorBrush(Color.FromRgb(221,200,143));
        static readonly Brush Upper=new SolidColorBrush(Color.FromRgb(142,202,187));
        public AscentChart(Ascent rocket){this.rocket=rocket;ClipToBounds=true;}
        protected override void OnRender(DrawingContext dc){
            double h=ActualHeight,w=ActualWidth;
            if(h<=20||w<=55)return;
            double seconds=Math.Max(60,Math.Ceiling(rocket.Time/60)*60);
            double km=Math.Max(rocket.Mission.Pe*1.12,rocket.Altitude*1.1), left=37, bottom=h-20;
            var grid=new Pen(Axis,1);
            dc.DrawRectangle(null,grid,new Rect(left,7,w-left-4,bottom-7));
            for(int i=1;i<=3;i++){
                double x=left+(w-left-4)*i/4;
                dc.DrawLine(grid,new Point(x,7),new Point(x,bottom));
            }
            double targetY=bottom-rocket.Mission.Pe/km*(bottom-7);
            dc.DrawLine(new Pen(Caption,1){DashStyle=DashStyles.Dash},new Point(left,targetY),new Point(w-4,targetY));
            Label(dc,rocket.Mission.Pe.ToString("0")+" km",0,targetY-5);
            Label(dc,"0",left,bottom+2);Label(dc,seconds.ToString("0")+" s",w-40,bottom+2);
            var core=new Pen(Core,1.7);var upper=new Pen(Upper,1.7);
            Point? previous=null;
            foreach(var sample in rocket.Track){
                var p=new Point(left+sample.Time/seconds*(w-left-4),bottom-Math.Max(0,sample.Altitude)/km*(bottom-7));
                if(previous.HasValue)dc.DrawLine(sample.Stage==0?core:upper,previous.Value,p);
                previous=p;
            }
            if(previous.HasValue)dc.DrawEllipse(Upper,null,previous.Value,3,3);
        }
        void Label(DrawingContext dc,string content,double x,double y){
            var text=new FormattedText(content,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                new Typeface("Consolas"),10,Caption,VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text,new Point(x,y));
        }
    }
}

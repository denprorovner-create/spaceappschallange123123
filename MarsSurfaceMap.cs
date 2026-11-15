using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Perilune {
    sealed class MarsSurfaceMap : FrameworkElement{
        readonly MarsFlight flight;
        readonly MarsSiteData site;
        readonly Action<Point> onWaypoint;
        readonly Func<Point,double> elevation;
        readonly Func<Point,Point> geographic;
        readonly BitmapSource regional;
        static readonly Brush BackgroundBrush=new SolidColorBrush(Color.FromRgb(20,23,33));
        static readonly Brush Paper=new SolidColorBrush(Color.FromRgb(239,237,233));
        static readonly Pen GridPen=new Pen(new SolidColorBrush(Color.FromRgb(99,79,78)),1);
        static readonly Pen RoutePen=new Pen(Brushes.LightGoldenrodYellow,1.6){DashStyle=DashStyles.Dash};
        static readonly Pen TrailPen=new Pen(new SolidColorBrush(Color.FromRgb(112,212,186)),1.65);
        Point? waypoint;
        public bool HasRegional {get{return regional!=null;}}
        public bool Regional {get;set;}
        public MarsSurfaceMap(MarsFlight flight,MarsSiteData site,Action<Point> onWaypoint,Func<Point,double> elevation,Func<Point,Point> geographic){
            this.flight=flight;this.site=site;this.onWaypoint=onWaypoint;this.elevation=elevation;this.geographic=geographic;
            if(site!=null&&!string.IsNullOrEmpty(site.ImagePath)){
                regional=new BitmapImage(new Uri(site.ImagePath));
                Regional=true;
            }
            RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
            MouseLeftButtonUp+=(s,e)=>{
                if(Regional)return;
                var p=e.GetPosition(this);var o=Origin;
                var at=new Point((p.X-o.X)/Scale,(o.Y-p.Y)/Scale);
                if(Math.Abs(at.X)<101&&at.Y>-37&&at.Y<124){onWaypoint(at);e.Handled=true;}
            };
            ClipToBounds=true;
            ToolTip="Use Traverse map to set a local waypoint.";
        }
        Point Origin {get{return new Point(RenderSize.Width*.50,135+124*Scale);}}
        double Scale {get{return Math.Max(.5,Math.Min((RenderSize.Height-212)/162,(RenderSize.Width-555)/204));}}
        Point Plot(Point world){var o=Origin;return new Point(o.X+world.X*Scale,o.Y-world.Y*Scale);}
        public void Update(Point? route){waypoint=route;InvalidateVisual();}
        void Text(DrawingContext dc,string value,Point point,double size=13){
            dc.DrawText(new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,Paper,VisualTreeHelper.GetDpi(this).PixelsPerDip),point);
        }
        protected override void OnRender(DrawingContext dc){
            dc.DrawRectangle(BackgroundBrush,null,new Rect(RenderSize));
            if(Regional&&regional!=null){
                RenderRegional(dc);
                return;
            }
            var a=Plot(new Point(-102,124));var b=Plot(new Point(102,-38));
            var field=new Rect(a,b);
            const int rows=25,columns=32;
            double shade=elevation==null?0:elevation(new Point());
            for(int z=0;z<rows;z++)for(int x=0;x<columns;x++){
                double ex=x*204d/columns-102,nz=z*162d/rows-38;
                double delta=elevation==null?Math.Sin(ex*.02)*8:elevation(new Point(ex,nz))-shade;
                double gradient=elevation==null?0:elevation(new Point(ex+3,nz-3))-elevation(new Point(ex-3,nz+3));
                int l=(int)Math.Max(-36,Math.Min(43,4*delta+48*gradient));
                var brush=new SolidColorBrush(Color.FromRgb((byte)(146+l),(byte)(100+l),(byte)(80+l)));
                var corner=Plot(new Point(ex,nz+162d/rows));
                dc.DrawRectangle(brush,null,new Rect(corner,new Size(field.Width/columns+.25,field.Height/rows+.25)));
            }
            dc.DrawRectangle(null,GridPen,field);
            for(int x=-100;x<=100;x+=20)dc.DrawLine(GridPen,Plot(new Point(x,-38)),Plot(new Point(x,124)));
            for(int z=-20;z<=120;z+=20)dc.DrawLine(GridPen,Plot(new Point(-102,z)),Plot(new Point(102,z)));
            foreach(var rock in MarsFlight.FieldBoulders)dc.DrawEllipse(Brushes.DarkSlateGray,null,Plot(rock),4,4);
            for(int i=Math.Max(1,flight.Traverse.Count-360);i<flight.Traverse.Count;i++)
                dc.DrawLine(TrailPen,Plot(flight.Traverse[i-1]),Plot(flight.Traverse[i]));
            foreach(var target in MarsFlight.Sites){
                var p=Plot(target.Point);dc.DrawEllipse(Brushes.SandyBrown,null,p,4.5,4.5);
                Text(dc,target.Name,p+new Vector(7,-11));
            }
            if(waypoint.HasValue){
                var p=Plot(waypoint.Value);
                dc.DrawLine(RoutePen,Plot(flight.Position),p);dc.DrawEllipse(null,RoutePen,p,9,9);
            }
            var rover=Plot(flight.Position);
            dc.DrawEllipse(Brushes.White,null,rover,5,5);
            dc.DrawLine(new Pen(Brushes.White,1.3),rover,rover+new Vector(19*Math.Sin(flight.Heading),-19*Math.Cos(flight.Heading)));
            Text(dc,"N ↑    /    20 m grid",new Point(field.Left,field.Top-23));
            Text(dc,"Click to set a waypoint. W/A/S/D returns control to the driver.",new Point(field.Left,field.Bottom+11));
            if(elevation!=null)Text(dc,string.Format("{0:N0} m elevation · MOLA plus simulated local relief",elevation(flight.Position)),new Point(field.Left,field.Bottom+30));
        }
        Point RegionalPoint(Point position,Rect bounds){
            return new Point(bounds.X+(position.X-site.ImageLeftMeters)/site.ImageWidthMeters*bounds.Width,
                bounds.Y+(site.ImageTopMeters-position.Y)/site.ImageHeightMeters*bounds.Height);
        }
        void RenderRegional(DrawingContext dc){
            double aspect=site.ImageWidthMeters/site.ImageHeightMeters;
            double height=Math.Max(30,Math.Min(RenderSize.Height-190,(RenderSize.Width-555)/aspect));
            double width=height*aspect;
            var bounds=new Rect((RenderSize.Width-width)/2,98,width,height);
            dc.DrawImage(regional,bounds);
            dc.DrawRectangle(null,GridPen,bounds);
            var target=RegionalPoint(new Point(),bounds);
            var rover=RegionalPoint(geographic(flight.Position),bounds);
            var pen=new Pen(Brushes.LightGoldenrodYellow,1.5);
            dc.DrawEllipse(null,pen,target,7,7);
            Text(dc,"Target",target+new Vector(9,5),12);
            if(bounds.Contains(rover)){
                dc.DrawEllipse(Brushes.Aquamarine,new Pen(Brushes.Black,1),rover,4.2,4.2);
                Text(dc,"Rover",rover+new Vector(9,-17),12);
            }else Text(dc,"Rover is outside this orbital tile",new Point(bounds.Left,bounds.Bottom+45),12);
            var scale=new Point(bounds.Left+12,bounds.Bottom-17);
            dc.DrawLine(new Pen(Brushes.White,2),scale,scale+new Vector(10000/site.ImageWidthMeters*width,0));
            Text(dc,"10 km",scale+new Vector(0,-16),10);
            Text(dc,"N ↑",new Point(bounds.Right-32,bounds.Top+8),12);
            Text(dc,"NASA Mars Trek · Viking orbital color mosaic",new Point(bounds.Left,bounds.Bottom+9));
            Text(dc,"Open Traverse map for rover routes and science sites.",new Point(bounds.Left,bounds.Bottom+27),12);
        }
    }
}

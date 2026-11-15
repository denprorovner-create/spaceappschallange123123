using System;
using System.IO;

namespace Perilune {
    // Real MGS/MOLA MEGDR elevations. Geographic coordinates are planetocentric,
    // east-positive; elevation is meters relative to the GMM3 areoid. Local x is
    // east and local z is north. Interpolation does not add spatial resolution.
    sealed class MarsSiteData {
        public static readonly MarsSiteData Hellas=new MarsSiteData();
        public readonly string Name="Hellas Deep Floor";
        public readonly double Latitude=-32.91015625,Longitude=62.19921875;
        public readonly double Elevation=-8208;
        // Hydrostatic reference estimate, not an observed or forecast pressure.
        public readonly double Pressure=636*Math.Exp(8208.0/11000);
        public readonly double PressureLow=400*Math.Exp(8208.0/11000),PressureHigh=870*Math.Exp(8208.0/11000);
        public readonly double ExtentMeters=50000;
        public readonly string SourcePath="assets/hellas-mola-128ppd.img";
        public readonly string RelativeImagePath="assets/hellas-viking-7-87-172.jpg";
        public readonly double NorthLatitude=-31,SouthLatitude=-34,WestLongitude=60,EastLongitude=64;
        public readonly int GridWidth=512,GridHeight=384,PixelsPerDegree=128;
        public readonly double ImageNorth=-32.34375,ImageSouth=-33.75,ImageWest=61.875,ImageEast=63.28125;
        public readonly int ImagePixels=256;
        public readonly double CenterSlopeDegrees=.8042,NeighborhoodMaxSlopeDegrees=11.6440;
        public readonly int CenterObservationCount=1;
        public readonly double NeighborhoodMinimum=-8208,NeighborhoodMaximum=-7902;
        public readonly double MetersPerDegree=3396000*Math.PI/180;
        public readonly double PixelNorthMeters,PixelEastMeters;
        public readonly string DataDescription="NASA MGS/MOLA median elevation · 128 px/degree · measured and interpolated bins";
        public readonly string ResolutionNote="MOLA sampling is about 463 × 389 m here. Rover-scale rocks, soil and hazards are simulated.";
        public readonly string SiteRationale="Deepest median elevation in the supplied MOLA product; a strong candidate for high surface pressure. The floor supports atmospheric and sediment studies, but nearby slopes and unresolved small hazards demand precise landing and cautious driving.";
        short[] heights;
        static short[] global;

        MarsSiteData(){
            PixelNorthMeters=MetersPerDegree/PixelsPerDegree;
            PixelEastMeters=PixelNorthMeters*Math.Cos(Latitude*Math.PI/180);
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,SourcePath.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(path)){
                byte[] bytes=File.ReadAllBytes(path);
                if(bytes.Length!=GridWidth*GridHeight*2)throw new InvalidDataException("The Hellas MOLA elevation crop has an unexpected size.");
                heights=Decode(bytes);
            }
        }
        static short[] Decode(byte[] data){
            var result=new short[data.Length/2];
            for(int i=0;i<result.Length;i++)result[i]=(short)((data[i*2]<<8)|data[i*2+1]);
            return result;
        }
        public bool HasElevationData {get{return heights!=null;}}
        public string ImagePath {
            get {
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,RelativeImagePath.Replace('/',Path.DirectorySeparatorChar));
                return File.Exists(path)?path:null;
            }
        }
        public double ImageLeftMeters {get{return (ImageWest-Longitude)*MetersPerDegree*Math.Cos(Latitude*Math.PI/180);}}
        public double ImageTopMeters {get{return (ImageNorth-Latitude)*MetersPerDegree;}}
        public double ImageWidthMeters {get{return (ImageEast-ImageWest)*MetersPerDegree*Math.Cos(Latitude*Math.PI/180);}}
        public double ImageHeightMeters {get{return (ImageNorth-ImageSouth)*MetersPerDegree;}}
        public double LatitudeAt(double northMeters){return Latitude+northMeters/MetersPerDegree;}
        public double LongitudeAt(double eastMeters){return Longitude+eastMeters/(MetersPerDegree*Math.Cos(Latitude*Math.PI/180));}
        public double EastAt(double longitude){return (longitude-Longitude)*MetersPerDegree*Math.Cos(Latitude*Math.PI/180);}
        public double NorthAt(double latitude){return (latitude-Latitude)*MetersPerDegree;}
        public bool Contains(double x,double z){
            double lat=LatitudeAt(z),lon=LongitudeAt(x);
            return lat<=NorthLatitude && lat>=SouthLatitude && lon>=WestLongitude && lon<=EastLongitude;
        }
        public double Height(double x,double z){
            if(heights==null)throw new FileNotFoundException("The NASA MOLA terrain file is missing. Keep the assets folder beside Perilune.exe.",SourcePath);
            double column=(LongitudeAt(x)-WestLongitude)*PixelsPerDegree-.5;
            double row=(NorthLatitude-LatitudeAt(z))*PixelsPerDegree-.5;
            column=Math.Max(0,Math.Min(GridWidth-1.000001,column));
            row=Math.Max(0,Math.Min(GridHeight-1.000001,row));
            int ix=(int)Math.Floor(column),iy=(int)Math.Floor(row);
            double u=column-ix,v=row-iy;
            double north=heights[iy*GridWidth+ix]*(1-u)+heights[iy*GridWidth+ix+1]*u;
            double south=heights[(iy+1)*GridWidth+ix]*(1-u)+heights[(iy+1)*GridWidth+ix+1]*u;
            return north*(1-v)+south*v;
        }
        public double SlopeDegrees(double x,double z){
            double east=(Height(x+PixelEastMeters,z)-Height(x-PixelEastMeters,z))/(2*PixelEastMeters);
            double north=(Height(x,z+PixelNorthMeters)-Height(x,z-PixelNorthMeters))/(2*PixelNorthMeters);
            return Math.Atan(Math.Sqrt(east*east+north*north))*180/Math.PI;
        }
        public double EstimatedPressure(double x,double z,double referencePa=636){
            return referencePa*Math.Exp(-Height(x,z)/11000);
        }
        public static double TraverseRelief(double east,double north){
            double window=Math.Max(0,Math.Min(1,(136-Math.Max(Math.Abs(east),Math.Abs(north)))/16));
            double ripple=.42*Math.Sin(east*.11+north*.16)+.30*Math.Cos(east*.18-north*.098);
            double rise=.82*Math.Cos((north-70)*.056)*Math.Sin((east+18)*.062);
            double blend=Math.Min(1,Math.Sqrt(east*east+north*north)/8);
            return (ripple+rise)*window*blend;
        }
        public static double GlobalElevation(double latitude,double longitude){
            if(global==null){
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","mars-mola-global-4ppd.img");
                byte[] bytes=File.ReadAllBytes(path);
                if(bytes.Length!=1440*720*2)throw new InvalidDataException("The global MOLA raster has an unexpected size.");
                global=Decode(bytes);
            }
            longitude=((longitude%360)+360)%360;
            int x=Math.Max(0,Math.Min(1439,(int)(longitude*4)));
            int y=Math.Max(0,Math.Min(719,(int)((90-latitude)*4)));
            return global[y*1440+x];
        }
    }
}

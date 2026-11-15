using System;

namespace Perilune {
    struct MarsAir {
        public readonly double StaticPressurePa,DensityKgM3,TemperatureK,SoundSpeedMps;
        public MarsAir(double pressure,double temperature){
            StaticPressurePa=pressure;
            TemperatureK=temperature;
            DensityKgM3=pressure/(MarsAtmosphere.GasConstant*temperature);
            SoundSpeedMps=Math.Sqrt(MarsAtmosphere.Gamma*MarsAtmosphere.GasConstant*temperature);
        }
    }

    sealed class MarsAtmosphere {
        public const double GasConstant=192.1, Gamma=1.29, MeanRadiusM=3389500, GravityAtDatum=3.727;
        public const double JezeroPressurePa=718, JezeroElevationM=-2500;
        const double SpacingM=250;
        const int Layers=1200;
        readonly double[] logPressure=new double[Layers+1];
        public readonly double SurfacePressurePa,DatumElevationM;
        // Smooth representative values, guided by the compiled temperature profile in the
        // NASA Mars 2020 atmospheric analysis (NTRS 20210024612), not a weather forecast.
        static readonly double[] HeightsM={-6500,0,10000,25000,40000,55000,70000,85000,125000,175000};
        static readonly double[] TemperaturesK={236,229,217,187,166,153,150,154,154,190};

        public MarsAtmosphere(double surfacePressurePa=JezeroPressurePa,double datumElevationM=JezeroElevationM){
            if(double.IsNaN(surfacePressurePa)||double.IsInfinity(surfacePressurePa)||surfacePressurePa<=0)
                throw new ArgumentOutOfRangeException("surfacePressurePa");
            if(double.IsNaN(datumElevationM)||Math.Abs(datumElevationM)>100000)
                throw new ArgumentOutOfRangeException("datumElevationM");
            SurfacePressurePa=surfacePressurePa;
            DatumElevationM=datumElevationM;
            logPressure[0]=Math.Log(SurfacePressurePa);
            for(int i=1;i<=Layers;i++){
                double midpoint=(i-.5)*SpacingM;
                logPressure[i]=logPressure[i-1]-SpacingM*InverseScaleHeight(midpoint);
            }
        }
        static double TemperatureAt(double heightAboveDatumM){
            int i=0;
            while(i+1<HeightsM.Length && heightAboveDatumM>=HeightsM[i+1])i++;
            if(i==HeightsM.Length-1||heightAboveDatumM<=HeightsM[0])return TemperaturesK[i];
            double t=(heightAboveDatumM-HeightsM[i])/(HeightsM[i+1]-HeightsM[i]);
            return TemperaturesK[i]+t*(TemperaturesK[i+1]-TemperaturesK[i]);
        }
        public double Radius(double heightAboveSiteM){return MeanRadiusM+DatumElevationM+heightAboveSiteM;}
        public double Gravity(double heightAboveSiteM){
            double radiusRatio=MeanRadiusM/Radius(heightAboveSiteM);
            return GravityAtDatum*radiusRatio*radiusRatio;
        }
        double InverseScaleHeight(double heightAboveSiteM){
            return Gravity(heightAboveSiteM)/(GasConstant*TemperatureAt(DatumElevationM+heightAboveSiteM));
        }
        public MarsAir Sample(double heightAboveSiteM){
            double height=Math.Max(-100000,heightAboveSiteM);
            if(height<0){
                double localPressure=SurfacePressurePa*Math.Exp(-height*InverseScaleHeight(height*.5));
                return new MarsAir(localPressure,TemperatureAt(DatumElevationM+height));
            }
            int layer=(int)(Math.Min(Layers,height/SpacingM));
            double pressure;
            if(layer==Layers){
                double extra=height-Layers*SpacingM;
                pressure=Math.Exp(logPressure[Layers]-extra*InverseScaleHeight(Layers*SpacingM));
            }else{
                double t=height/SpacingM-layer;
                pressure=Math.Exp(logPressure[layer]+t*(logPressure[layer+1]-logPressure[layer]));
            }
            return new MarsAir(pressure,TemperatureAt(DatumElevationM+height));
        }
    }
}

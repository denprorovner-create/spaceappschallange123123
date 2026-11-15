using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Windows.Media.Media3D;

namespace Perilune {
    sealed class SolarSystem {
        public const double AU=149597870.7;
        public static readonly string[] Names={"Sun","Mercury","Venus","Earth","Moon","Mars","Jupiter","Saturn","Uranus","Neptune"};
        public static readonly double[] GM={132712440041.27942,22031.868551,324858.592,398600.435507,4902.800118,42828.375816,126712764.1,37940584.8418,5794556.4,6836527.10058};
        public readonly Vector3D[] Positions=new Vector3D[10], Velocities=new Vector3D[10];
        readonly Vector3D[] accel=new Vector3D[10];
        public double Time {get;private set;}
        public SolarSystem(){
            string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","horizons-20261114.csv");
            var rows=File.ReadAllLines(path);
            if(rows.Length!=Names.Length)throw new InvalidDataException("Incomplete Horizons state table.");
            for(int i=0;i<rows.Length;i++){
                var col=rows[i].Split(',');
                if(col[0]!=Names[i] || col.Length!=7)throw new InvalidDataException("Unexpected Horizons body row.");
                var n=col.Skip(1).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
                Positions[i]=new Vector3D(n[0],n[2],n[1]);
                Velocities[i]=new Vector3D(n[3],n[5],n[4]);
            }
            Acceleration();
        }
        SolarSystem(SolarSystem original){
            Array.Copy(original.Positions,Positions,Positions.Length);
            Array.Copy(original.Velocities,Velocities,Velocities.Length);
            Array.Copy(original.accel,accel,accel.Length);
            Time=original.Time;
        }
        public SolarSystem Copy(){return new SolarSystem(this);}
        public SolarSystem CopyAt(double time,Func<int,Vector3D> position,Func<int,Vector3D> velocity){
            var snapshot=Copy();
            snapshot.Time=time;
            for(int i=0;i<GM.Length;i++){
                snapshot.Positions[i]=position(i);
                snapshot.Velocities[i]=velocity(i);
            }
            snapshot.Acceleration();
            return snapshot;
        }
        static Vector3D Force(Vector3D r,double gm){
            double d2=r.LengthSquared;
            return r*(gm/(d2*Math.Sqrt(d2)));
        }
        void Acceleration(){
            Array.Clear(accel,0,accel.Length);
            for(int i=0;i<GM.Length;i++)for(int j=i+1;j<GM.Length;j++){
                var d=Force(Positions[j]-Positions[i],1);
                accel[i]+=d*GM[j];accel[j]-=d*GM[i];
            }
        }
        public void Advance(double duration){
            while(duration>1e-7){
                double dt=Math.Min(1800,duration);
                for(int i=0;i<GM.Length;i++){
                    Velocities[i]+=accel[i]*(dt*.5);
                    Positions[i]+=Velocities[i]*dt;
                }
                Acceleration();
                for(int i=0;i<GM.Length;i++)Velocities[i]+=accel[i]*(dt*.5);
                duration-=dt;Time+=dt;
            }
        }
        public Vector3D RelativeGravity(Body body,Vector3D ship,double t){
            int primary=body.Lunar?4:3;
            Vector3D a=Force(-ship,GM[primary]);
            for(int j=0;j<GM.Length;j++){
                if(j==primary)continue;
                var r=Positions[j]-Positions[primary]+(Velocities[j]-Velocities[primary])*(t-Time);
                a+=Force(r-ship,GM[j])-Force(r,GM[j]);
            }
            return a;
        }
        public double MomentumError(Vector3D original){
            return (Momentum()-original).Length;
        }
        public Vector3D Momentum(){
            Vector3D p=new Vector3D();
            for(int i=0;i<GM.Length;i++)p+=GM[i]*Velocities[i];
            return p;
        }
    }
}


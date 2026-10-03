using System;
using System.IO;
using System.IO.Pipes;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

public class CameraData {
    public float[] values;
    public bool Project(float x,float y,float z,out float sx,out float sy) {
        var c=values;float dx=x-c[0],dy=y-c[1],dz=z-c[2];
        float depth=dx*c[3]+dy*c[4]+dz*c[5];sx=sy=0;
        if(depth<=1)return false;
        sx=c[12]+(dx*c[6]+dy*c[7]+dz*c[8])*c[14]/depth;
        sy=c[13]-(dx*c[9]+dy*c[10]+dz*c[11])*c[15]/depth;
        return !float.IsNaN(sx)&&!float.IsInfinity(sx)&&!float.IsNaN(sy)&&!float.IsInfinity(sy);
    }
}
public sealed class PipeReceiver : IDisposable {
    volatile Frame latest;volatile bool stopped;NamedPipeServerStream server;readonly object gate=new object();
    public Frame Latest { get { return latest; } }
    long collectionUs,readyFrames;
    public double MeanCollectionMs {get{return System.Threading.Interlocked.Read(ref collectionUs)/1000.0/Math.Max(1,System.Threading.Interlocked.Read(ref readyFrames));}}
    public long Received,ProjectionChecks;public float MaxProjectionError;public string Error="";
    public PipeReceiver(){var thread=new Thread(ReadLoop){IsBackground=true,Name="ESP pipe receiver"};thread.Start();}
    static float F(string value){return float.Parse(value,CultureInfo.InvariantCulture);}
    public static Frame ReadFrame(TextReader reader,Dictionary<string,Joint[]> bones,ref string world) {
        string line=reader.ReadLine();if(line==null)return null;
        string[] h=line.Split('\t');if(h.Length!=30||h[0]!="F")throw new InvalidDataException("Invalid frame header");
        if(world!=h[2]){bones.Clear();world=h[2];}
        var frame=new Frame { seq=int.Parse(h[1]),status=h[3],width=int.Parse(h[4]),height=int.Parse(h[5]),total=int.Parse(h[6]),civilians=int.Parse(h[7]),evidence=int.Parse(h[8]),reports=int.Parse(h[9]),updateMs=int.Parse(h[10]),collectMs=F(h[11]),bonesCostMs=F(h[12]),healthCostMs=F(h[13]) };
        // Camera payload is 16 floats; timing values consume three slots, so the header has 30 fields.
        frame.camera=new CameraData { values=new float[16] };
        for(int i=0;i<16;i++)frame.camera.values[i]=F(h[14+i]);
        var heads=new Dictionary<string,Joint>();
        var boxes=new List<Box>();var items=new List<Marker>();var ids=new List<string>();var itemIds=new List<string>();
        while((line=reader.ReadLine())!=null) {
            if(line.Length==0)continue;
            if(line=="E"){
                for(int i=0;i<boxes.Count;i++){Joint[] b;if(bones.TryGetValue(ids[i],out b))boxes[i].bones=b;Joint head;if(heads.TryGetValue(ids[i],out head))boxes[i].head=head;}
                for(int i=0;i<items.Count;i++){Joint[] b;if(bones.TryGetValue(itemIds[i],out b))items[i].bones=b;}
                frame.boxes=boxes.ToArray();frame.items=items.ToArray();return frame;
            }
            string[] p=line.Split('\t');if(p.Length==0)continue;
            if(p[0]=="S"&&p.Length==50) {
                var joints=new Joint[16];for(int i=0;i<16;i++){int offset=2+i*3;bool valid=p[offset]!="_";joints[i]=new Joint{world=true,valid=valid,x=valid?F(p[offset]):0,y=valid?F(p[offset+1]):0,z=valid?F(p[offset+2]):0};}bones[p[1]]=joints;
            }else if(p[0]=="A"&&p.Length==2) {
                int ammo=int.Parse(p[1],CultureInfo.InvariantCulture);if(ammo<0||ammo>10000)throw new InvalidDataException("Invalid ammo count");frame.ammo=ammo;
            }else if(p[0]=="H"&&p.Length==5) {
                heads[p[1]]=new Joint{world=true,valid=true,x=F(p[2]),y=F(p[3]),z=F(p[4])};
            }else if(p[0]=="B"&&p.Length==11) {
                float x=F(p[2]),y=F(p[3]),z=F(p[4]),half=F(p[5]),tx,ty,bx,by;
                if(!frame.camera.Project(x,y,z+half,out tx,out ty)||!frame.camera.Project(x,y,z-half,out bx,out by))continue;
                float height=Math.Abs(by-ty);if(height<2||height>frame.height*3)continue;
                var box=new Box { x=(tx+bx)*.5f-height*.24f,y=Math.Min(ty,by),w=height*.48f,h=height,d=F(p[6]),kind=p[7],state=p[8],hp=F(p[9]),maxhp=F(p[10]) };
                boxes.Add(box);ids.Add(p[1]);
            }else if(p[0]=="M"&&p.Length==8) {
                float x,y;if(!frame.camera.Project(F(p[2]),F(p[3]),F(p[4]),out x,out y))continue;
                items.Add(new Marker{x=x,y=y,d=F(p[5]),kind=p[6],name=p[7]});itemIds.Add(p[1]);
            }else if(p[0]=="G"&&p.Length==3) { frame.solo=p[1]=="1";frame.station=p[2]=="1"; }
            else if(p[0]=="V"&&p.Length==3) { frame.projectionError=F(p[1]);frame.projectionChecks=int.Parse(p[2]); }
            else throw new InvalidDataException("Invalid record: "+p[0]);
            if(boxes.Count+items.Count>2048)throw new InvalidDataException("Too many records");
        }
        return null; // A partial frame is never published.
    }
    void ReadLoop() {
        while(!stopped) {
            try {
                using(var pipe=new NamedPipeServerStream("RoNUnifiedESP_Fast_v1",PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536)) {
                    lock(gate){if(stopped)return;server=pipe;}pipe.WaitForConnection();Error="";
                    using(var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,32768)) {
                        var bones=new Dictionary<string,Joint[]>();string world=null;Frame next;
                        while(!stopped&&(next=ReadFrame(reader,bones,ref world))!=null){if(next.status=="ready"){Interlocked.Add(ref collectionUs,(long)(next.collectMs*1000));Interlocked.Increment(ref readyFrames);}if(next.projectionChecks>0){ProjectionChecks+=next.projectionChecks;MaxProjectionError=Math.Max(MaxProjectionError,next.projectionError);}latest=next;Interlocked.Increment(ref Received);}
                    }
                }
            }catch(Exception ex){if(!stopped){Error=ex.Message;Thread.Sleep(100);}}
            finally {lock(gate){server=null;}}
        }
    }
    public void Dispose(){stopped=true;lock(gate){if(server!=null)server.Dispose();}}
}

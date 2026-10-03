using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Small, bounded command history: a short click must retain both press and release.
public sealed class StationInput : IDisposable {
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 readonly string path,session=Guid.NewGuid().ToString("N");
 readonly Queue<string> events=new Queue<string>();
 int seq,frameSeq=-1;bool eligible,down,armed;DateTime received=DateTime.MinValue,written=DateTime.MinValue;
 public string Error="";
 public StationInput(string root){path=Path.Combine(root,"station-input.txt");Publish(false,false,DateTime.UtcNow);}
 public void Update(Frame frame,Options options,bool nativeActive,bool menu,bool foreground){
  var now=DateTime.UtcNow;
  if(frame!=null&&frame.seq!=frameSeq){frameSeq=frame.seq;received=now;}
  bool enabled=frame!=null&&frame.status=="ready"&&frame.station&&(now-received).TotalSeconds<1&&options.stationFire&&!options.freecam&&nativeActive&&!menu&&foreground;
  Sample(enabled,(GetAsyncKeyState(1)&0x8000)!=0,now);
 }
 public void Sample(bool enabled,bool physicalDown,DateTime now){
  bool changed=enabled!=eligible;
  if(!enabled){armed=false;physicalDown=false;}
  else if(!eligible){armed=!physicalDown;physicalDown=false;}
  else if(!armed){if(!physicalDown)armed=true;physicalDown=false;}
  bool nextDown=enabled&&armed&&physicalDown;
  if(nextDown!=down){down=nextDown;events.Enqueue((++seq)+" "+(down?"1":"0"));while(events.Count>8)events.Dequeue();changed=true;}
  eligible=enabled;
  if(changed||(enabled&&(now-written).TotalMilliseconds>=250))Publish(enabled,down,now);
 }
 void Publish(bool enabled,bool pressed,DateTime now){
  try{
   long unix=(long)(now-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalSeconds;
   var text=new StringBuilder().Append(session).Append(' ').Append(unix).Append(' ').Append(enabled?'1':'0').Append(' ').Append(seq).Append(' ').Append(pressed?'1':'0').Append('\n');
   foreach(var e in events)text.Append(e).Append('\n');
   string temp=path+".tmp";File.WriteAllText(temp,text.ToString(),new UTF8Encoding(false));
   if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
   written=now;Error="";
  }catch(Exception e){Error=e.Message;}
 }
 public void Dispose(){Sample(false,false,DateTime.UtcNow);Publish(false,false,DateTime.UtcNow);}
}

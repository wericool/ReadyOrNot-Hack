using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
public sealed class NativeGuard : IDisposable {
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr read);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteProcessMemory(IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr written);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protection,out uint previous);
 [DllImport("kernel32.dll")]static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 const string Hash="3431A1CB1F2103756BBB9F65D54DB666FE325AA95A60386145B8EA9E6398A86F";
 class Site {public long rva;public byte[] original;public bool applied,station;}
 readonly Site[] sites={
  new Site{rva=0x4def620,original=Bytes("4488442418555641554156488dac2458")},
  new Site{rva=0x4b5ea70,original=Bytes("48895c2408574883ec200fb6fa488bd9"),station=true},
  new Site{rva=0x4b5df10,original=Bytes("8891c5200000c3cccccccccccccccccc"),station=true},
  new Site{rva=0x4b5ec20,original=Bytes("48895c240848896c2410488974241848"),station=true}
 };
 IntPtr handle;long module;int pid,seq=-1;DateTime received=DateTime.MinValue,check=DateTime.MinValue,retry=DateTime.MinValue;
 public string Error="";public bool StationActive,RevengeActive;
 static byte[] Bytes(string hex){var b=new byte[hex.Length/2];for(int i=0;i<b.Length;i++)b[i]=Convert.ToByte(hex.Substring(i*2,2),16);return b;}
 byte[] Read(long address,int size){var b=new byte[size];UIntPtr n;if(!ReadProcessMemory(handle,(IntPtr)address,b,(UIntPtr)(uint)size,out n)||n.ToUInt64()!=(ulong)size)throw new IOException("Не удалось прочитать код игры");return b;}
 void Validate(Site site){var b=Read(module+site.rva,site.original.Length);for(int i=0;i<b.Length;i++)if(b[i]!=(site.applied&&i==0?(byte)0xc3:site.original[i]))throw new IOException("Код игры отличается: "+site.rva.ToString("X"));}
 void Apply(Site site,bool enabled){
  if(site.applied==enabled)return;Validate(site);IntPtr address=(IntPtr)(module+site.rva);uint old;
  if(!VirtualProtectEx(handle,address,(UIntPtr)1,0x40,out old))throw new IOException("Защита памяти игры недоступна");
  try{UIntPtr n;if(!WriteProcessMemory(handle,address,new[]{enabled?(byte)0xc3:site.original[0]},(UIntPtr)1,out n)||n.ToUInt64()!=1)throw new IOException("Изменение памяти не выполнено");site.applied=enabled;FlushInstructionCache(handle,address,(UIntPtr)1);}
  finally{uint ignored;VirtualProtectEx(handle,address,(UIntPtr)1,old,out ignored);}
  Validate(site);
 }
 void Attach(){
  foreach(var p in Process.GetProcessesByName("ReadyOrNotSteam-Win64-Shipping"))using(p){
   if(p.Id==pid&&handle!=IntPtr.Zero)return;
   Dispose();pid=p.Id;
   using(var sha=SHA256.Create())using(var stream=File.OpenRead(p.MainModule.FileName))if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","")!=Hash)throw new IOException("Игровые функции не поддерживают эту версию EXE");
   module=p.MainModule.BaseAddress.ToInt64();handle=OpenProcess(0x38,false,p.Id);if(handle==IntPtr.Zero)throw new IOException("Нет доступа к процессу игры");
   foreach(var site in sites)Validate(site);return;
  }
  Dispose();
 }
 public void Update(Frame frame,Options options){
  if(frame!=null&&frame.seq!=seq){seq=frame.seq;received=DateTime.UtcNow;}
  if((DateTime.UtcNow-check).TotalMilliseconds<100)return;check=DateTime.UtcNow;
  if(DateTime.UtcNow<retry)return;
  bool fresh=frame!=null&&(DateTime.UtcNow-received).TotalSeconds<1&&frame.status=="ready";
  bool station=fresh&&frame.station&&options.stationFire,revenge=fresh&&options.noRetaliation;
  try{
   if(!station&&!revenge&&handle==IntPtr.Zero)return;
   Attach();if(handle==IntPtr.Zero)return;
   foreach(var site in sites)Apply(site,site.station?station:revenge);
   StationActive=station;RevengeActive=revenge;Error="";
  }catch(Exception e){Error=e.Message;Restore();retry=DateTime.UtcNow.AddSeconds(5);}
 }
 void Restore(){foreach(var site in sites)if(site.applied)try{Apply(site,false);}catch(Exception e){Error=e.Message;}StationActive=false;RevengeActive=false;}
 public void Dispose(){Restore();if(handle!=IntPtr.Zero)CloseHandle(handle);handle=IntPtr.Zero;pid=0;foreach(var site in sites)site.applied=false;}
}

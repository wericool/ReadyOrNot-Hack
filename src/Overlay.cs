using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class Frame { public int ammo=-1; public bool solo,station;public int updateMs, seq, width, height, total, civilians, evidence, reports; public float collectMs,bonesCostMs,healthCostMs,projectionError; public int projectionChecks; public CameraData camera; public string status; public Box[] boxes; public Marker[] items; }
public class Marker { public float x,y,d; public string kind,name; public Joint[] bones; }
public class Joint { public float x,y,z; public bool valid,world; }
public class Box { public float x,y,w,h,d,hp,maxhp; public string kind,state; public Joint[] bones; public Joint head; }
public class Options { public bool enabled=true, distance=true, civilians=true, health=true, healthbar=true, status=true, skeleton=true, boxes=true, headDot=false, names=true, ammo=true, inactive=false, evidence=true, reports=true, traps=true, arrested=false, freecam=false, noRetaliation=false, stationFire=false; public int ammoX=100, ammoY=100, range=150, color=0, updateMs=16, bonesMs=100, healthMs=250, renderMs=16; }
public class Overlay : Form {
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
    readonly GpuWindow gpu=new GpuWindow();
    readonly PipeReceiver receiver=new PipeReceiver();
    readonly NativeGuard native=new NativeGuard();
    readonly StationInput stationInput=new StationInput(AppDomain.CurrentDomain.BaseDirectory);
    readonly Timer inputTimer=new Timer();
    readonly Timer lifetimeTimer=new Timer { Interval=250 };
    Process watchedGame;
    [DllImport("user32.dll")]static extern uint RegisterWindowMessage(string name);
    static readonly uint stopMessage=RegisterWindowMessage("RoNSoloESP.Stop");
    double paintMs; DateTime diagnosticAt=DateTime.MinValue;
    bool redraw=true;int ammoSlider=-1;
    struct RECT { public int l,t,r,b; }
    struct POINT { public int x,y; }
    static readonly int[] updateIntervals={100,50,33,16,8};
    readonly Timer refreshTimer=new Timer();
    readonly string root=AppDomain.CurrentDomain.BaseDirectory;
    readonly JavaScriptSerializer json=new JavaScriptSerializer();
    readonly Font text=new Font("Segoe UI",12), title=new Font("Segoe UI",16,FontStyle.Bold), hint=new Font("Segoe UI",10);
    readonly Color[] colors={Color.FromArgb(255,75,90),Color.FromArgb(70,230,255),Color.FromArgb(255,214,70)};
    readonly string[] groups={"ESP","Персонажи","Отображение","Объекты","Игрок","Скорость","Инфо"};
    static readonly int[,] links={{0,1},{1,2},{2,3},{2,4},{4,5},{5,6},{2,7},{7,8},{8,9},{3,10},{10,11},{11,12},{3,13},{13,14},{14,15}};
    double dataHz; DateTime rateStart=DateTime.UtcNow; long rateReceived;
    Options options=new Options(); Frame frame; DateTime lastFrame=DateTime.MinValue;
    bool menu=true, active, hotkeys; int selected=0, group=0; IntPtr game; int ticks;
    readonly int[] hotkeyCodes={0x2D,0x75,0x2E,0x26,0x28,0x25,0x27,0x0D,0x09,0x76};
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x80000|0x20|0x08000000|0x80; return p; } }
    public Overlay() {
        Text="Ready or Not hack by ericool"; FormBorderStyle=FormBorderStyle.None;
        BackColor=Color.Magenta; TransparencyKey=Color.Magenta; TopMost=true;
        ShowInTaskbar=false; DoubleBuffered=true; AutoScaleMode=AutoScaleMode.None;
        try {
            string saved=File.ReadAllText(Path.Combine(root,"settings.json"));
            options=json.Deserialize<Options>(saved);
            if(options!=null&&!json.Deserialize<Dictionary<string,object>>(saved).ContainsKey("healthbar"))options.healthbar=options.health;
        } catch { }
        if(options==null)options=new Options();
        options.freecam=false;options.noRetaliation=false;options.stationFire=false;Save();
        options.range=Math.Max(25,Math.Min(500,options.range)); options.color=Math.Max(0,options.color)%3;
        if(Array.IndexOf(updateIntervals,options.updateMs)<0)options.updateMs=16;
        options.renderMs=Math.Max(8,Math.Min(100,options.renderMs));
        watchedGame=Launcher.RunningGame();
        lifetimeTimer.Tick+=(s,e)=>{if(GameHasExited())Close();};lifetimeTimer.Start();
        refreshTimer.Interval=options.renderMs;refreshTimer.Tick+=(s,e)=>TickOverlay();refreshTimer.Start();
        inputTimer.Interval=8;inputTimer.Tick+=(s,e)=>{stationInput.Update(receiver.Latest,options,native.StationActive,menu,game!=IntPtr.Zero&&GetForegroundWindow()==game&&!IsIconic(game));DragAmmoSlider();};inputTimer.Start();
        gpu.Click=(x,y,right)=>OnMouseDown(new MouseEventArgs(right?MouseButtons.Right:MouseButtons.Left,1,x,y,0));
    }
    void BindKeys(bool bind) {
        if(bind==hotkeys)return;
        for(int i=0;i<hotkeyCodes.Length;i++) {
            if(bind && (i<3||i==9||menu))RegisterHotKey(Handle,100+i,0x4000,(uint)hotkeyCodes[i]);
            else UnregisterHotKey(Handle,100+i);
        }
        hotkeys=bind;
    }
    void RebindMenu() { BindKeys(false); BindKeys(active); }
    protected override void WndProc(ref Message m) {
        if(m.Msg==stopMessage){Close();return;}
        if(m.Msg==0x0084) {
            long point=m.LParam.ToInt64();
            var client=PointToClient(new Point((short)(point&65535),(short)((point>>16)&65535)));
            m.Result=(IntPtr)(menu&&active&&MenuBounds().Contains(client)?1:-1);return;
        }
        if(m.Msg==0x0021){m.Result=(IntPtr)3;return;}
        if(m.Msg==0x0312 && active) {
            int id=m.WParam.ToInt32()-100;
            if(id==0){menu=!menu;RebindMenu();}
            if(id==1){options.enabled=!options.enabled;Save();}
            if(id==2){Close();return;}
            if(id==9){options.freecam=!options.freecam;Save();}
            if(menu) {
                if(id==3)selected=(selected+RowCount()-1)%RowCount();
                if(id==4)selected=(selected+1)%RowCount();
                if(id==8){group=(group+1)%groups.Length;selected=0;}
                if(id>=5&&id<=7)ChangeOption(id==5?-1:1);

            }
            redraw=true; return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { lifetimeTimer.Dispose();if(watchedGame!=null)watchedGame.Dispose();inputTimer.Dispose();stationInput.Dispose();options.freecam=false;options.noRetaliation=false;options.stationFire=false;Save();native.Dispose();BindKeys(false);receiver.Dispose();gpu.Close();refreshTimer.Dispose();text.Dispose();title.Dispose();hint.Dispose();base.OnFormClosed(e); }
    protected override void OnShown(EventArgs e){base.OnShown(e);Hide();}
    Rectangle MenuBounds() { return new Rectangle(24,Math.Max(12,Math.Min(50,Height-490)),Math.Min(760,Math.Max(300,Width-48)),460); }
    bool GameHasExited() {
        if(watchedGame==null)return false;
        try { return watchedGame.HasExited; } catch(InvalidOperationException) { return true; }
    }
    int RowCount() { return Rows().Length; }
    string[] Rows() {
        if(group==0)return new[]{"ESP: "+OnOff(options.enabled),"Дальность: "+options.range+" м","Цвет врагов: "+new[]{"красный","голубой","жёлтый"}[options.color]};
        if(group==1)return new[]{"Гражданские: "+OnOff(options.civilians),"Арестованные: "+OnOff(options.arrested),"Мёртвые / без сознания: "+OnOff(options.inactive),"Подписи персонажей: "+OnOff(options.names),"Статусы: "+OnOff(options.status),"Расстояние: "+OnOff(options.distance)};
        if(group==2)return new[]{"Рамки: "+OnOff(options.boxes),"Скелет: "+OnOff(options.skeleton),"Точка головы врага: "+OnOff(options.headDot),"ХП числом: "+OnOff(options.health),"Полоска ХП: "+OnOff(options.healthbar)};
        if(group==3)return new[]{"Улики / брошенное оружие: "+OnOff(options.evidence),"Пострадавшие / для доклада: "+OnOff(options.reports),"Растяжки у дверей: "+OnOff(options.traps)};
        if(group==4)return new[]{"Патроны в магазине: "+OnOff(options.ammo),"Фрикам (F7): "+OnOff(options.freecam),"Без мести SWAT: "+OnOff(options.noRetaliation),"Оружие в участке: "+OnOff(options.stationFire),"Патроны — горизонталь: "+options.ammoX+"%","Патроны — вертикаль: "+options.ammoY+"%"};
        if(group==6){
            bool fresh=frame!=null&&(DateTime.UtcNow-lastFrame).TotalMilliseconds<600;
            string state=!fresh?"Ожидание данных игры":frame.status=="ready"?"Подключено":frame.status=="no_pawn"?"Загрузите миссию":frame.status;
            return new[]{"Версия: 1.0 · "+state,
                "Подозреваемые: "+(fresh?frame.total:0)+" · Гражданские: "+(fresh?frame.civilians:0),
                "Улики: "+(fresh?frame.evidence:0)+" · Для доклада: "+(fresh?frame.reports:0),
                "Данные: "+Math.Round(dataHz)+" Гц · Сбор: "+(fresh?frame.collectMs:0).ToString("0.00")+" мс",
                "Средний сбор: "+receiver.MeanCollectionMs.ToString("0.00")+" мс · GPU tier: "+gpu.Tier,
                "CPU-команды: "+paintMs.ToString("0.00")+" мс · Кости: "+(fresh?frame.bonesCostMs:0).ToString("0.00")+" мс",
                "Состояния: "+(fresh?frame.healthCostMs:0).ToString("0.00")+" мс · Ошибка проекции: "+receiver.MaxProjectionError.ToString("0.00")};
        }
        return new[]{"Координаты: "+options.updateMs+" мс","Кости: "+options.bonesMs+" мс","Здоровье / состояния: "+options.healthMs+" мс","Отрисовка: "+options.renderMs+" мс"};
    }
    void ChangeOption(int direction) {
        if(group==6)return;
        if(group==0){
            if(selected==0)options.enabled=!options.enabled;
            if(selected==1)options.range=Math.Max(25,Math.Min(500,options.range+direction*25));
            if(selected==2)options.color=(options.color+(direction<0?2:1))%3;
        }else if(group==1){
            if(selected==0)options.civilians=!options.civilians;
            if(selected==1)options.arrested=!options.arrested;
            if(selected==2)options.inactive=!options.inactive;
            if(selected==3)options.names=!options.names;
            if(selected==4)options.status=!options.status;
            if(selected==5)options.distance=!options.distance;
        }else if(group==2){
            if(selected==0)options.boxes=!options.boxes;
            if(selected==1)options.skeleton=!options.skeleton;
            if(selected==2)options.headDot=!options.headDot;
            if(selected==3)options.health=!options.health;
            if(selected==4)options.healthbar=!options.healthbar;
        }else if(group==3){
            if(selected==0)options.evidence=!options.evidence;
            if(selected==1)options.reports=!options.reports;
            if(selected==2)options.traps=!options.traps;
        }else if(group==4){
            if(selected==0)options.ammo=!options.ammo;
            if(selected==1)options.freecam=!options.freecam;
            if(selected==2)options.noRetaliation=!options.noRetaliation;
            if(selected==3)options.stationFire=!options.stationFire;
            if(selected==4)options.ammoX=Math.Max(0,Math.Min(100,options.ammoX+direction*5));
            if(selected==5)options.ammoY=Math.Max(0,Math.Min(100,options.ammoY+direction*5));
        }else{
            if(selected==0)options.updateMs=Cycle(options.updateMs,updateIntervals,direction);
            if(selected==1)options.bonesMs=Cycle(options.bonesMs,new[]{500,200,100,50,33},direction);
            if(selected==2)options.healthMs=Cycle(options.healthMs,new[]{1000,500,250,100},direction);
            if(selected==3){options.renderMs=Cycle(options.renderMs,updateIntervals,direction);refreshTimer.Interval=options.renderMs;}
        }
        Save();redraw=true;
    }
    static int Cycle(int value,int[] values,int direction){int index=Array.IndexOf(values,value);if(index<0)index=0;return values[(index+direction+values.Length)%values.Length];}
    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);if(!menu||!active)return;
        var bounds=MenuBounds();int x=e.X-bounds.X,y=e.Y-bounds.Y;
        if(y>=60&&y<100){group=Math.Max(0,Math.Min(groups.Length-1,x/(bounds.Width/groups.Length)));selected=0;redraw=true;return;}
        int row=(y-115)/37;
        if(group==4&&(row==4||row==5)&&y>=115){selected=row;redraw=true;if(e.Button==MouseButtons.Left&&x>=bounds.Width-260){ammoSlider=row-4;SetAmmoSlider(row-4,e.X);}return;}
        if(y>=115&&row>=0&&row<RowCount()){selected=row;ChangeOption(e.Button==MouseButtons.Right?-1:1);}
    }
    void SetAmmoSlider(int axis,int clientX){
        var bounds=MenuBounds();float start=bounds.X+bounds.Width-260,end=bounds.Right-26;
        int value=Math.Max(0,Math.Min(100,(int)Math.Round((clientX-start)*100/(end-start))));
        if(axis==0)options.ammoX=value;else options.ammoY=value;redraw=true;
    }
    void DragAmmoSlider(){
        if(ammoSlider<0)return;
        if(!menu||group!=4||GetForegroundWindow()!=game||(GetAsyncKeyState(1)&0x8000)==0){ammoSlider=-1;Save();return;}
        POINT cursor;if(GetCursorPos(out cursor))SetAmmoSlider(ammoSlider,PointToClient(new Point(cursor.x,cursor.y)).X);
    }
    PointF AmmoPosition(){
        float mx=Math.Min(24,Math.Max(0,(Width-160)/2f)),my=Math.Min(30,Math.Max(0,(Height-64)/2f));
        return new PointF(mx+Math.Max(0,Width-160-2*mx)*Math.Max(0,Math.Min(100,options.ammoX))/100f,my+Math.Max(0,Height-64-2*my)*Math.Max(0,Math.Min(100,options.ammoY))/100f);
    }
    void Save() { try { File.WriteAllText(Path.Combine(root,"settings.json"),json.Serialize(options)); } catch { } }
    void TickOverlay() {
        if(GameHasExited()){Close();return;}
        if (++ticks%60==1 || game==IntPtr.Zero) {
            if(watchedGame==null)watchedGame=Launcher.RunningGame();
            try { if(watchedGame!=null){watchedGame.Refresh();game=watchedGame.MainWindowHandle;} } catch { game=IntPtr.Zero; }
        }
        native.Update(receiver.Latest,options);
        active=game!=IntPtr.Zero && GetForegroundWindow()==game && !IsIconic(game);
        BindKeys(active);
        if(!active) { if(gpu.IsVisible)gpu.Hide();return; }
        RECT r; POINT pt=new POINT();
        if(!GetClientRect(game,out r)||!ClientToScreen(game,ref pt))return;
        var bounds=new Rectangle(pt.x,pt.y,r.r-r.l,r.b-r.t);
        if(bounds.Width<20||bounds.Height<20)return;
        if(Bounds!=bounds)Bounds=bounds;
        POINT cursor;bool interactive=menu&&GetCursorPos(out cursor)&&MenuBounds().Contains(PointToClient(new Point(cursor.x,cursor.y)));
        gpu.Place(bounds,interactive);
        var next=receiver.Latest;
        if(next!=null&&!object.ReferenceEquals(frame,next)) {
            redraw=true;frame=next;lastFrame=DateTime.UtcNow;
            double elapsed=(lastFrame-rateStart).TotalSeconds;if(elapsed>=1){long received=receiver.Received;dataHz=(received-rateReceived)/elapsed;rateReceived=received;rateStart=lastFrame;}
        }
        if(redraw){var paint=Stopwatch.StartNew();gpu.Render(DrawFrame);paint.Stop();paintMs=paint.Elapsed.TotalMilliseconds;redraw=false;}
        if((DateTime.UtcNow-diagnosticAt).TotalSeconds>=1){
            diagnosticAt=DateTime.UtcNow;
            try{File.WriteAllText(Path.Combine(root,"diagnostics.json"),json.Serialize(new { seq=frame==null?0:frame.seq,status=frame==null?"waiting":frame.status,dataHz=dataHz,received=receiver.Received,collectMs=frame==null?0:frame.collectMs,meanCollectMs=receiver.MeanCollectionMs,bonesCostMs=frame==null?0:frame.bonesCostMs,healthCostMs=frame==null?0:frame.healthCostMs,paintCommandMs=paintMs,gpuTier=gpu.Tier,projectionError=receiver.MaxProjectionError,projectionChecks=receiver.ProjectionChecks,boxes=frame==null?0:frame.boxes.Length,items=frame==null?0:frame.items.Length,transport="named-pipe",stationInputError=stationInput.Error,stationNativeActive=native.StationActive,revengeNativeActive=native.RevengeActive,nativeError=native.Error,error=receiver.Error }));}catch{}
        }
    }

    void DrawHeadDot(GpuGraphics g,Box box,float sx,float sy,Color color) {
        if(box.kind!="suspect"||box.head==null||!box.head.valid||frame.camera==null)return;
        float x,y;if(!frame.camera.Project(box.head.x,box.head.y,box.head.z,out x,out y))return;
        x*=sx;y*=sy;if(x<0||y<0||x>=Width||y>=Height)return;
        using(var border=new SolidBrush(Color.Black))g.FillEllipse(border,x-4,y-4,8,8);
        using(var dot=new SolidBrush(color))g.FillEllipse(dot,x-2.5f,y-2.5f,5,5);
    }
    void DrawSkeleton(GpuGraphics g,Joint[] bones,float sx,float sy,Color color,float maxLengthSquared) {
        if(bones==null)return;
        using(var outline=new Pen(Color.Black,4))
        using(var pen=new Pen(color,2)) {
            for(int i=0;i<links.GetLength(0);i++) {
                int a=links[i,0],z=links[i,1];if(a>=bones.Length||z>=bones.Length)continue;
                var p=bones[a];var q=bones[z];if(p==null||q==null||!p.valid||!q.valid)continue;
                float ax=p.x,ay=p.y,bx=q.x,by=q.y;
                if(p.world&&frame!=null&&frame.camera!=null){if(!frame.camera.Project(p.x,p.y,p.z,out ax,out ay)||!frame.camera.Project(q.x,q.y,q.z,out bx,out by))continue;}
                float dx=(ax-bx)*sx,dy=(ay-by)*sy;if(dx*dx+dy*dy>maxLengthSquared)continue;
                g.DrawLine(outline,ax*sx,ay*sy,bx*sx,by*sy);
                g.DrawLine(pen,ax*sx,ay*sy,bx*sx,by*sy);
            }
        }
    }
    void Label(GpuGraphics g,string value,float x,float y,Color color,Font font) {
        using(var shadow=new SolidBrush(Color.Black))g.DrawString(value,font,shadow,x+1,y+1);
        using(var brush=new SolidBrush(color))g.DrawString(value,font,brush,x,y);
    }
    void DrawFrame(GpuGraphics g) {
        if(!active)return;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        bool fresh=frame!=null&&(DateTime.UtcNow-lastFrame).TotalMilliseconds<600;
        if(fresh&&frame.status=="ready"&&options.enabled&&frame.width>0&&frame.height>0&&frame.boxes!=null) {
            float sx=(float)ClientSize.Width/frame.width,sy=(float)ClientSize.Height/frame.height;
            var occupied=new List<RectangleF>();
            foreach(var b in frame.boxes) {
                if(b.d>options.range||b.w<=0||b.h<=0)continue;
                if(b.kind=="civilian"&&!options.civilians)continue;
                if(b.state=="arrested"&&!options.arrested)continue;
                if(!options.inactive&&(b.state=="dead"||b.state=="unconscious"))continue;
                Color color=b.kind=="civilian"?Color.FromArgb(70,240,125):colors[options.color%3];
                if(b.state=="dead"||b.state=="unconscious"||b.state=="arrested")color=Color.Silver;
                var rect=new RectangleF(b.x*sx,b.y*sy,b.w*sx,b.h*sy);
                if(rect.Right<0||rect.Bottom<0||rect.Left>Width||rect.Top>Height)continue;
                if(options.headDot)DrawHeadDot(g,b,sx,sy,color);
                if(options.boxes) {
                    using(var black=new Pen(Color.Black,4))g.DrawRectangle(black,rect.X,rect.Y,rect.Width,rect.Height);
                    using(var pen=new Pen(color,2))g.DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);
                }
                if(options.skeleton)DrawSkeleton(g,b.bones,sx,sy,color,rect.Height*rect.Height*4);
                string label=options.names?(b.kind=="civilian"?"Гражд.":"Подозр."):"";
                if(options.distance)label+=(label.Length>0?" · ":"")+Math.Round(b.d)+" м";
                var labels=new List<string>();if(label.Length>0)labels.Add(label);
                if(options.status) {
                    string state=b.state=="surrendered"?"Сдался":b.state=="arrested"?"Арестован":b.state=="unconscious"?"Без сознания":b.state=="dead"?"Мёртв":"Активен";
                    labels.Add(state);
                }
                if(options.healthbar&&b.maxhp>0) {
                    float ratio=Math.Max(0,Math.Min(1,b.hp/b.maxhp));
                    using(var brush=new SolidBrush(Color.Black))g.FillRectangle(brush,rect.X-9,rect.Y-1,6,rect.Height+2);
                    using(var brush=new SolidBrush(Color.FromArgb((int)(255*(1-ratio)),(int)(230*ratio),60)))g.FillRectangle(brush,rect.X-8,rect.Bottom-rect.Height*ratio,4,rect.Height*ratio);
                }
                if(options.health&&b.maxhp>0)labels.Add(Math.Round(b.hp)+" / "+Math.Round(b.maxhp)+" HP");
                if(labels.Count==0)continue;
                float lw=labels.Max(value=>g.MeasureString(value,hint).Width)+8,lh=labels.Count*19+4;
                var panel=new RectangleF(Math.Max(2,Math.Min(Width-lw-2,rect.X+rect.Width/2-lw/2)),rect.Y-lh-5,lw,lh);
                for(int attempt=0;attempt<30&&occupied.Any(area=>area.IntersectsWith(panel));attempt++)panel.Y-=lh+5;
                if(panel.Y<2)panel.Y=rect.Bottom+8;
                occupied.Add(panel);
                using(var line=new Pen(color,1))g.DrawLine(line,rect.X+rect.Width/2,rect.Y,panel.X+panel.Width/2,panel.Bottom);
                using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,panel);
                for(int i=0;i<labels.Count;i++)Label(g,labels[i],panel.X+4,panel.Y+2+i*19,i==labels.Count-1&&options.health?Color.White:color,hint);
            }
            foreach(var item in frame.items??new Marker[0]) {
                if(item.d>options.range||(item.kind=="evidence"?!options.evidence:item.kind=="trap"?!options.traps:!options.reports))continue;
                float px=item.x*sx,py=item.y*sy;
                if(px<0||py<0||px>Width||py>Height)continue;
                Color color=item.kind=="trap"?Color.FromArgb(255,100,45):item.kind=="evidence"?Color.Gold:Color.FromArgb(80,220,255);
                if(options.skeleton)DrawSkeleton(g,item.bones,sx,sy,color,Width*Width+Height*Height);
                var diamond=item.kind=="trap"?new[]{new PointF(px,py-10),new PointF(px+10,py+8),new PointF(px-10,py+8),new PointF(px,py-10)}:new[]{new PointF(px,py-7),new PointF(px+7,py),new PointF(px,py+7),new PointF(px-7,py),new PointF(px,py-7)};
                using(var pen=new Pen(Color.Black,4))g.DrawLines(pen,diamond);
                using(var pen=new Pen(color,2))g.DrawLines(pen,diamond);
                string value=(item.kind=="trap"?"РАСТЯЖКА: ":item.kind=="evidence"?"Улика: ":item.kind=="body"?"Тело: ":"Сообщить: ")+(item.name??"Объект");
                if(options.distance)value+=" · "+Math.Round(item.d)+" м";
                float lw=g.MeasureString(value,hint).Width+8;
                var panel=new RectangleF(Math.Max(2,Math.Min(Width-lw-2,px-lw/2)),py-32,lw,23);
                for(int attempt=0;attempt<40&&occupied.Any(area=>area.IntersectsWith(panel));attempt++)panel.Y-=28;
                if(panel.Y<2)panel.Y=py+10;
                occupied.Add(panel);
                using(var pen=new Pen(color,1))g.DrawLine(pen,px,py,panel.X+panel.Width/2,panel.Bottom);
                using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,panel);
                Label(g,value,panel.X+4,panel.Y+2,color,hint);
            }
        }
        if(fresh&&frame.status=="ready"&&options.ammo&&frame.ammo>=0){
            var ammoPosition=AmmoPosition();float ax=ammoPosition.X,ay=ammoPosition.Y;
            using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,ax,ay,160,64);
            Label(g,"В магазине",ax+12,ay+8,Color.LightGray,hint);
            Label(g,frame.ammo.ToString(),ax+12,ay+28,frame.ammo==0?Color.FromArgb(255,75,90):Color.White,title);
        }
        if(!menu)return;
        var boundsMenu=MenuBounds();float x=boundsMenu.X,y=boundsMenu.Y,w=boundsMenu.Width;
        using(var bg=new SolidBrush(Color.FromArgb(22,25,33)))g.FillRectangle(bg,boundsMenu);
        using(var pen=new Pen(colors[options.color],2))g.DrawRectangle(pen,boundsMenu);
        Label(g,"Ready or Not hack by ericool",x+18,y+12,Color.White,title);
        for(int i=0;i<groups.Length;i++) {
            var tab=new RectangleF(x+i*w/groups.Length,y+60,w/groups.Length,38);
            using(var brush=new SolidBrush(i==group?Color.FromArgb(52,62,80):Color.FromArgb(30,34,44)))g.FillRectangle(brush,tab);
            Label(g,groups[i],tab.X+10,tab.Y+8,i==group?colors[options.color]:Color.LightGray,hint);
        }
        var rows=Rows();
        for(int i=0;i<rows.Length;i++) {
            if(group!=6&&i==selected)using(var brush=new SolidBrush(Color.FromArgb(38,44,57)))g.FillRectangle(brush,x+10,y+112+i*37,w-20,34);
            Label(g,(group!=6&&i==selected?"›  ":"   ")+rows[i],x+18,y+115+i*37,group!=6&&i==selected?colors[options.color]:Color.White,text);
            if(group==4&&(i==4||i==5)){
                float start=x+w-260,end=x+w-26,cy=y+127+i*37;
                int value=i==4?options.ammoX:options.ammoY;
                using(var bar=new SolidBrush(Color.FromArgb(70,80,95)))g.FillRectangle(bar,start,cy-2,end-start,4);
                using(var fill=new SolidBrush(colors[options.color])){g.FillRectangle(fill,start,cy-2,(end-start)*value/100f,4);g.FillEllipse(fill,start+(end-start)*value/100f-5,cy-5,10,10);}
            }
        }
        if(group==6){
            string error=native.Error.Length>0?native.Error:stationInput.Error.Length>0?stationInput.Error:receiver.Error;
            if(!string.IsNullOrEmpty(error))Label(g,error,x+18,y+375,Color.FromArgb(255,120,90),hint);
        }
        Label(g,"Insert — меню · F6 — ESP · F7 — фрикам · Delete — выход",x+18,y+406,Color.LightGray,hint);
        Label(g,"Мышь / Tab — раздел · ↑↓ — выбор · ←→ / Enter — изменить",x+18,y+430,Color.LightGray,hint);
    }
    static string OnOff(bool value) { return value?"ВКЛ":"ВЫКЛ"; }
    [STAThread] public static void Main() {
        bool created; using(var mutex=new System.Threading.Mutex(true,"Local\\RoNSoloESP",out created)) {
            if(!created){MessageBox.Show("ESP уже запущен. Insert — меню, Delete — выход.","Ready or Not hack by ericool");return;}
            SetProcessDPIAware(); Application.EnableVisualStyles();
            try { Launcher.Run(); } catch(Exception ex) { MessageBox.Show(ex.Message,"Ready or Not hack by ericool",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using D=System.Drawing;
using W=System.Windows.Media;

public sealed class GpuGraphics {
    readonly DrawingContext context;
    static readonly Dictionary<int,W.SolidColorBrush> brushes=new Dictionary<int,W.SolidColorBrush>();
    static readonly Dictionary<string,W.Pen> pens=new Dictionary<string,W.Pen>();
    static readonly Dictionary<string,FormattedText> texts=new Dictionary<string,FormattedText>();
    public GpuGraphics(DrawingContext dc){context=dc;}
    public object TextRenderingHint {set{}}
    static W.SolidColorBrush Brush(D.Color color){W.SolidColorBrush brush;int id=color.ToArgb();if(!brushes.TryGetValue(id,out brush)){brush=new W.SolidColorBrush(W.Color.FromArgb(color.A,color.R,color.G,color.B));brush.Freeze();brushes[id]=brush;}return brush;}
    static W.Pen Pen(D.Pen pen){string key=pen.Color.ToArgb()+"/"+pen.Width;W.Pen result;if(!pens.TryGetValue(key,out result)){result=new W.Pen(Brush(pen.Color),pen.Width);result.Freeze();pens[key]=result;}return result;}
    static FormattedText Text(string value,D.Font font,D.Color color){
        string key=value+"\0"+font.Name+"/"+font.Size+"/"+font.Bold+"/"+color.ToArgb();FormattedText text;
        if(!texts.TryGetValue(key,out text)){
            if(texts.Count>2048)texts.Clear();
            text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(new W.FontFamily(font.Name),FontStyles.Normal,font.Bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),font.SizeInPoints*96/72,Brush(color),1.0);
            texts[key]=text;
        }
        return text;
    }
    public SizeF MeasureString(string value,D.Font font){var text=Text(value,font,D.Color.White);return new SizeF((float)text.WidthIncludingTrailingWhitespace,(float)text.Height);}
    public void DrawString(string value,D.Font font,D.Brush brush,float x,float y){context.DrawText(Text(value,font,((D.SolidBrush)brush).Color),new System.Windows.Point(x,y));}
    public void DrawLine(D.Pen pen,float x,float y,float a,float b){context.DrawLine(Pen(pen),new System.Windows.Point(x,y),new System.Windows.Point(a,b));}
    public void DrawLines(D.Pen pen,D.PointF[] points){for(int i=1;i<points.Length;i++)DrawLine(pen,points[i-1].X,points[i-1].Y,points[i].X,points[i].Y);}
    public void DrawRectangle(D.Pen pen,float x,float y,float w,float h){if(w>0&&h>0)context.DrawRectangle(null,Pen(pen),new Rect(x,y,w,h));}
    public void DrawRectangle(D.Pen pen,D.Rectangle rect){DrawRectangle(pen,rect.X,rect.Y,rect.Width,rect.Height);}
    public void FillEllipse(D.Brush brush,float x,float y,float w,float h){if(w>0&&h>0)context.DrawEllipse(Brush(((D.SolidBrush)brush).Color),null,new System.Windows.Point(x+w/2,y+h/2),w/2,h/2);}
    public void FillRectangle(D.Brush brush,float x,float y,float w,float h){if(w>0&&h>0)context.DrawRectangle(Brush(((D.SolidBrush)brush).Color),null,new Rect(x,y,w,h));}
    public void FillRectangle(D.Brush brush,D.Rectangle rect){FillRectangle(brush,rect.X,rect.Y,rect.Width,rect.Height);}
    public void FillRectangle(D.Brush brush,D.RectangleF rect){FillRectangle(brush,rect.X,rect.Y,rect.Width,rect.Height);}
}
public sealed class GpuWindow : Window {
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int hgt,uint flags);
    readonly VisualHost host=new VisualHost();IntPtr handle;HwndSource source;D.Rectangle previousBounds;double previousScaleX,previousScaleY;
    public Func<int,int,bool> Hit;public Action<int,int,bool> Click;
    public int Tier { get {return RenderCapability.Tier>>16;} }
    public GpuWindow(){Title="Ready Or Not Experimental GPU";WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=W.Brushes.Transparent;ShowInTaskbar=false;Topmost=true;ShowActivated=false;Focusable=false;Content=host;Width=1;Height=1;
        SourceInitialized+=(s,e)=>{handle=new WindowInteropHelper(this).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Hook);int style=GetWindowLong(handle,-20);SetWindowLong(handle,-20,style|0x08000000|0x80|0x20);};
    }
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled){
        if(message==0x21){handled=true;return (IntPtr)3;}
        if(message==0x201||message==0x204){long p=lp.ToInt64();if(Click!=null)Click((short)(p&65535),(short)((p>>16)&65535),message==0x204);handled=true;}
        return IntPtr.Zero;
    }
    public void Place(D.Rectangle bounds,bool interactive){bool shown=!IsVisible;if(shown)Show();if(shown||bounds!=previousBounds){SetWindowPos(handle,IntPtr.Zero,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x14);previousBounds=bounds;}
        int style=GetWindowLong(handle,-20);int updated=interactive?style&~0x20:style|0x20;if(style!=updated)SetWindowLong(handle,-20,updated);
        if(source!=null){var matrix=source.CompositionTarget.TransformToDevice;if(matrix.M11!=previousScaleX||matrix.M22!=previousScaleY){host.Visual.Transform=new ScaleTransform(1/matrix.M11,1/matrix.M22);previousScaleX=matrix.M11;previousScaleY=matrix.M22;}}
    }
    public void Render(Action<GpuGraphics> draw){using(var context=host.Visual.RenderOpen())draw(new GpuGraphics(context));}
    public DrawingVisual Visual { get {return host.Visual;} }
    sealed class VisualHost : FrameworkElement {
        public readonly DrawingVisual Visual=new DrawingVisual();public VisualHost(){AddVisualChild(Visual);AddLogicalChild(Visual);}
        protected override int VisualChildrenCount {get{return 1;}}
        protected override Visual GetVisualChild(int index){return Visual;}
    }
}

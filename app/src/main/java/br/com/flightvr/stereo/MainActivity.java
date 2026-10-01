package br.com.flightvr.stereo;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Bundle;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public class MainActivity extends Activity implements SensorEventListener {
    private static final int BG = 0xff070b12;
    private static final int PANEL = 0xff141b26;
    private static final int TEXT = 0xfff4f7fb;
    private static final int MUTED = 0xff91a0b4;
    private static final int ACCENT = 0xff64e6be;

    private SensorManager sensorManager;
    private Sensor rotationSensor;
    private final float[] baseR = new float[9];
    private final float[] currentR = new float[9];
    private boolean haveBase = false;
    private boolean trackingWanted = false;
    private boolean haveSmoothed = false;
    private double smoothYaw, smoothPitch, smoothRoll;
    private long lastSensorNs = 0L;

    private DatagramSocket udpSocket;
    private InetAddress udpTarget;
    private final ExecutorService udpExecutor = Executors.newSingleThreadExecutor();

    private SharedPreferences prefs;
    private EditText hostField, portField, hzField, sensField, smoothField, urlField, ipdField;
    private TextView statusView, poseView;
    private WebView webView;
    private StereoCalibrationView stereoView;
    private boolean inViewer = false;

    private String host = "192.168.0.2";
    private int port = 4242;
    private int hz = 90;
    private double sensitivity = 1.0;
    private double smoothing = 0.12;
    private double ipdMm = 64.0;

    @Override public void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        prefs = getSharedPreferences("cfg", MODE_PRIVATE);
        sensorManager = (SensorManager)getSystemService(SENSOR_SERVICE);
        rotationSensor = sensorManager.getDefaultSensor(Sensor.TYPE_GAME_ROTATION_VECTOR);
        if (rotationSensor == null) rotationSensor = sensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR);
        loadPrefs();
        buildSetup();
        immersive(false);
    }

    private void loadPrefs() {
        host = prefs.getString("host", "192.168.0.2");
        port = prefs.getInt("port", 4242);
        hz = prefs.getInt("hz", 90);
        sensitivity = Double.longBitsToDouble(prefs.getLong("sens", Double.doubleToLongBits(1.0)));
        smoothing = Double.longBitsToDouble(prefs.getLong("smooth", Double.doubleToLongBits(0.12)));
        ipdMm = Double.longBitsToDouble(prefs.getLong("ipd", Double.doubleToLongBits(64.0)));
    }

    private TextView text(String value, float sp, int color) {
        TextView v = new TextView(this);
        v.setText(value); v.setTextSize(sp); v.setTextColor(color);
        return v;
    }

    private EditText field(String value) {
        EditText e = new EditText(this);
        e.setText(value); e.setTextColor(TEXT); e.setSingleLine(true);
        e.setBackgroundColor(PANEL); e.setPadding(16, 10, 16, 10);
        return e;
    }

    private Button button(String label) {
        Button b = new Button(this); b.setText(label); b.setTextSize(12); return b;
    }

    private LinearLayout column() {
        LinearLayout l = new LinearLayout(this); l.setOrientation(LinearLayout.VERTICAL); return l;
    }

    private void addLabeled(LinearLayout parent, String label, EditText field) {
        TextView t = text(label, 11, MUTED); t.setPadding(0, 8, 0, 3); parent.addView(t); parent.addView(field);
    }

    private void buildSetup() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout root = column(); root.setPadding(22, 16, 22, 22); root.setBackgroundColor(BG);
        scroll.addView(root);

        TextView title = text("FLIGHTVR STEREO 2.0", 23, TEXT); title.setTypeface(null, 1); root.addView(title);
        root.addView(text("Android HMD client • head tracking + visor side-by-side para MSFS 2020", 12, MUTED));

        LinearLayout net = new LinearLayout(this); net.setOrientation(LinearLayout.HORIZONTAL); net.setPadding(0, 12, 0, 0); root.addView(net);
        LinearLayout left = column(), right = column();
        net.addView(left, new LinearLayout.LayoutParams(0, -2, 2));
        net.addView(right, new LinearLayout.LayoutParams(0, -2, 1));
        left.setPadding(0,0,10,0);
        hostField = field(host); portField = field(String.valueOf(port));
        addLabeled(left,"IP DO PC / OPENTRACK",hostField); addLabeled(right,"PORTA UDP",portField);

        LinearLayout tune = new LinearLayout(this); tune.setOrientation(LinearLayout.HORIZONTAL); tune.setPadding(0,8,0,0); root.addView(tune);
        LinearLayout c1=column(), c2=column(), c3=column(), c4=column();
        tune.addView(c1,new LinearLayout.LayoutParams(0,-2,1)); tune.addView(c2,new LinearLayout.LayoutParams(0,-2,1));
        tune.addView(c3,new LinearLayout.LayoutParams(0,-2,1)); tune.addView(c4,new LinearLayout.LayoutParams(0,-2,1));
        c1.setPadding(0,0,8,0); c2.setPadding(0,0,8,0); c3.setPadding(0,0,8,0);
        hzField=field(String.valueOf(hz)); sensField=field(String.format(Locale.US,"%.2f",sensitivity));
        smoothField=field(String.format(Locale.US,"%.2f",smoothing)); ipdField=field(String.format(Locale.US,"%.1f",ipdMm));
        addLabeled(c1,"TRACKING Hz",hzField); addLabeled(c2,"SENSIBILIDADE",sensField);
        addLabeled(c3,"SUAVIZAÇÃO",smoothField); addLabeled(c4,"IPD (mm)",ipdField);

        urlField = field(prefs.getString("url", "http://192.168.0.2:8080/vr"));
        addLabeled(root,"URL DO STREAM SBS — deve conter olho esquerdo e direito diferentes",urlField);

        LinearLayout r1 = new LinearLayout(this); r1.setOrientation(LinearLayout.HORIZONTAL); r1.setPadding(0,12,0,0); root.addView(r1);
        Button start=button("▶ INICIAR TRACKING"), rec=button("◎ RECENTRALIZAR"), test=button("◉ TESTE ESTÉREO");
        r1.addView(start,new LinearLayout.LayoutParams(0,-2,1)); r1.addView(rec,new LinearLayout.LayoutParams(0,-2,1)); r1.addView(test,new LinearLayout.LayoutParams(0,-2,1));
        start.setOnClickListener(v -> startTracking()); rec.setOnClickListener(v -> recenter()); test.setOnClickListener(v -> showCalibration());

        LinearLayout r2 = new LinearLayout(this); r2.setOrientation(LinearLayout.HORIZONTAL); root.addView(r2);
        Button viewer=button("🥽 ABRIR VISOR SBS"), stop=button("■ PARAR TRACKING");
        r2.addView(viewer,new LinearLayout.LayoutParams(0,-2,2)); r2.addView(stop,new LinearLayout.LayoutParams(0,-2,1));
        viewer.setOnClickListener(v -> { saveFields(); if (!trackingWanted) startTracking(); showViewer(); });
        stop.setOnClickListener(v -> stopTracking());

        statusView=text("STATUS: PARADO",14,ACCENT); statusView.setPadding(0,12,0,2); root.addView(statusView);
        poseView=text("YAW 0.0°   PITCH 0.0°   ROLL 0.0°",17,TEXT); poseView.setTypeface(android.graphics.Typeface.MONOSPACE); root.addView(poseView);

        TextView note=text("Para estéreo verdadeiro, o PC precisa enviar um quadro SBS com duas perspectivas diferentes. O app não duplica a mesma imagem. VOL+ = recentralizar • VOL− = voltar ao menu.",11,MUTED);
        note.setPadding(0,10,0,0); root.addView(note);

        setContentView(scroll); inViewer=false;
    }

    private void saveFields() {
        host=hostField.getText().toString().trim();
        port=parseInt(portField,4242); hz=Math.max(30,Math.min(120,parseInt(hzField,90)));
        sensitivity=parseDouble(sensField,1.0); smoothing=Math.max(0,Math.min(.95,parseDouble(smoothField,.12)));
        ipdMm=Math.max(50,Math.min(78,parseDouble(ipdField,64.0)));
        prefs.edit().putString("host",host).putInt("port",port).putInt("hz",hz)
            .putLong("sens",Double.doubleToLongBits(sensitivity)).putLong("smooth",Double.doubleToLongBits(smoothing))
            .putLong("ipd",Double.doubleToLongBits(ipdMm)).putString("url",urlField.getText().toString().trim()).apply();
    }

    private int parseInt(EditText e,int d){ try{return Integer.parseInt(e.getText().toString().trim());}catch(Exception x){return d;} }
    private double parseDouble(EditText e,double d){ try{return Double.parseDouble(e.getText().toString().trim().replace(',','.'));}catch(Exception x){return d;} }

    private void startTracking() {
        saveFields();
        if (rotationSensor==null) { toast("Este celular não expõe Rotation Vector/Giroscópio compatível."); return; }
        try {
            udpTarget=InetAddress.getByName(host);
            if (udpSocket==null || udpSocket.isClosed()) udpSocket=new DatagramSocket();
        } catch(Exception e) { setStatus("ERRO DE REDE: "+e.getMessage()); return; }
        trackingWanted=true; haveBase=false; haveSmoothed=false;
        sensorManager.unregisterListener(this);
        sensorManager.registerListener(this,rotationSensor,SensorManager.SENSOR_DELAY_FASTEST);
        setStatus("ATIVO → "+host+":"+port+" @ "+hz+" Hz");
    }

    private void stopTracking(){ trackingWanted=false; sensorManager.unregisterListener(this); setStatus("PARADO"); }
    private void recenter(){ haveBase=false; haveSmoothed=false; toast("Centro redefinido"); }
    private void toast(String s){ Toast.makeText(this,s,Toast.LENGTH_SHORT).show(); }
    private void setStatus(final String s){ runOnUiThread(() -> { if(statusView!=null) statusView.setText("STATUS: "+s); }); }

    @Override public void onSensorChanged(SensorEvent event) {
        if(!trackingWanted) return;
        long now=System.nanoTime(); long interval=1_000_000_000L/Math.max(30,hz);
        if(lastSensorNs!=0 && now-lastSensorNs<interval) return; lastSensorNs=now;
        float[] raw=new float[9]; SensorManager.getRotationMatrixFromVector(raw,event.values); remap(raw,currentR);
        if(!haveBase){ System.arraycopy(currentR,0,baseR,0,9); haveBase=true; return; }
        float[] rel=multiplyTransposeLeft(baseR,currentR); float[] ori=new float[3]; SensorManager.getOrientation(rel,ori);
        double yaw=Math.toDegrees(ori[0])*sensitivity, pitch=Math.toDegrees(ori[1])*sensitivity, roll=Math.toDegrees(ori[2])*sensitivity;
        if(!haveSmoothed){ smoothYaw=yaw; smoothPitch=pitch; smoothRoll=roll; haveSmoothed=true; }
        else { double g=1.0-smoothing; smoothYaw=smoothAngle(smoothYaw,yaw,g); smoothPitch=smoothAngle(smoothPitch,pitch,g); smoothRoll=smoothAngle(smoothRoll,roll,g); }
        sendPose(smoothYaw,smoothPitch,smoothRoll);
        if(poseView!=null) poseView.setText(String.format(Locale.US,"YAW %6.1f°   PITCH %6.1f°   ROLL %6.1f°",smoothYaw,smoothPitch,smoothRoll));
        if(stereoView!=null) stereoView.invalidate();
    }

    private void remap(float[] in,float[] out){
        int rot=getWindowManager().getDefaultDisplay().getRotation(); int x=SensorManager.AXIS_X,y=SensorManager.AXIS_Y;
        if(rot==Surface.ROTATION_90){x=SensorManager.AXIS_Y;y=SensorManager.AXIS_MINUS_X;}
        else if(rot==Surface.ROTATION_180){x=SensorManager.AXIS_MINUS_X;y=SensorManager.AXIS_MINUS_Y;}
        else if(rot==Surface.ROTATION_270){x=SensorManager.AXIS_MINUS_Y;y=SensorManager.AXIS_X;}
        SensorManager.remapCoordinateSystem(in,x,y,out);
    }

    private float[] multiplyTransposeLeft(float[] a,float[] b){
        float[] r=new float[9];
        for(int i=0;i<3;i++) for(int j=0;j<3;j++){ float sum=0; for(int k=0;k<3;k++) sum+=a[k*3+i]*b[k*3+j]; r[i*3+j]=sum; }
        return r;
    }

    private double smoothAngle(double prev,double next,double gain){ double d=((next-prev+540.0)%360.0)-180.0; return prev+d*gain; }

    private void sendPose(final double yaw,final double pitch,final double roll){
        if(udpSocket==null||udpTarget==null) return;
        final DatagramSocket s=udpSocket; final InetAddress target=udpTarget; final int p=port;
        udpExecutor.execute(() -> {
            try {
                ByteBuffer bb=ByteBuffer.allocate(48).order(ByteOrder.LITTLE_ENDIAN);
                bb.putDouble(0).putDouble(0).putDouble(0).putDouble(yaw).putDouble(pitch).putDouble(roll);
                byte[] bytes=bb.array(); s.send(new DatagramPacket(bytes,bytes.length,target,p));
            } catch(Exception ignored) {}
        });
    }

    private void showViewer(){
        saveFields(); inViewer=true; stereoView=null; immersive(true);
        FrameLayout frame=new FrameLayout(this); frame.setBackgroundColor(Color.BLACK);
        webView=new WebView(this); WebSettings ws=webView.getSettings();
        ws.setJavaScriptEnabled(true); ws.setDomStorageEnabled(true); ws.setMediaPlaybackRequiresUserGesture(false);
        ws.setLoadWithOverviewMode(true); ws.setUseWideViewPort(true); ws.setBuiltInZoomControls(false);
        webView.setWebChromeClient(new WebChromeClient()); webView.setBackgroundColor(Color.BLACK);
        frame.addView(webView,new FrameLayout.LayoutParams(-1,-1));
        TextView hint=text("VOL+ RECENTER   •   VOL− MENU",10,0x99ffffff); hint.setGravity(Gravity.CENTER);
        FrameLayout.LayoutParams hp=new FrameLayout.LayoutParams(-1,36,Gravity.BOTTOM); frame.addView(hint,hp);
        setContentView(frame);
        String u=prefs.getString("url","http://192.168.0.2:8080/vr");
        webView.loadUrl(u);
        hint.postDelayed(() -> hint.setVisibility(View.GONE),3500);
    }

    private void showCalibration(){
        saveFields(); inViewer=true; immersive(true); webView=null;
        stereoView=new StereoCalibrationView(this); setContentView(stereoView);
    }

    private void showMenu(){
        if(webView!=null){ webView.stopLoading(); webView.destroy(); webView=null; }
        stereoView=null; immersive(false); buildSetup();
    }

    private void immersive(boolean on){
        if(on) getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN|View.SYSTEM_UI_FLAG_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY|View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN|View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION|View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
        else getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_VISIBLE);
    }

    @Override public boolean onKeyDown(int keyCode,KeyEvent e){
        if(keyCode==KeyEvent.KEYCODE_VOLUME_UP){ recenter(); return true; }
        if(keyCode==KeyEvent.KEYCODE_VOLUME_DOWN && inViewer){ showMenu(); return true; }
        return super.onKeyDown(keyCode,e);
    }

    @Override public void onBackPressed(){ if(inViewer) showMenu(); else super.onBackPressed(); }
    @Override protected void onResume(){ super.onResume(); if(trackingWanted && rotationSensor!=null) sensorManager.registerListener(this,rotationSensor,SensorManager.SENSOR_DELAY_FASTEST); }
    @Override protected void onPause(){ sensorManager.unregisterListener(this); super.onPause(); }
    @Override protected void onDestroy(){ if(udpSocket!=null) udpSocket.close(); udpExecutor.shutdownNow(); super.onDestroy(); }
    @Override public void onAccuracyChanged(Sensor sensor,int accuracy){}

    private class StereoCalibrationView extends View {
        private final Paint line=new Paint(1), dim=new Paint(1), txt=new Paint(1);
        private final int[][] edges={{0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}};
        private float downX;
        StereoCalibrationView(Context c){ super(c); line.setColor(Color.WHITE); line.setStrokeWidth(3); dim.setColor(0xff526070); dim.setStrokeWidth(2); txt.setColor(0xffaebbd0); txt.setTextSize(22); setBackgroundColor(Color.BLACK); }
        @Override protected void onDraw(Canvas c){ super.onDraw(c); int w=getWidth(),h=getHeight(),half=w/2; drawEye(c,0,half,h,-1); drawEye(c,half,half,h,1); c.drawLine(half,0,half,h,dim); c.drawText("IPD "+String.format(Locale.US,"%.1f mm",ipdMm),20,30,txt); c.drawText("VOL+ RECENTER • VOL− MENU",half+20,30,txt); postInvalidateDelayed(16); }
        private void drawEye(Canvas c,int x0,int ew,int h,int eyeSign){
            float cx=x0+ew/2f, cy=h/2f; c.drawCircle(cx,cy,7,line);
            double eye=eyeSign*(ipdMm/64.0)*0.035;
            for(int z=1;z<=8;z++){
                float[] a=project(-1.4,-.8,z,eye,ew,h), b=project(1.4,-.8,z,eye,ew,h);
                if(a!=null&&b!=null)c.drawLine(x0+a[0],a[1],x0+b[0],b[1],dim);
            }
            for(int xi=-4;xi<=4;xi++){
                float[] a=project(xi*.35,-.8,1,eye,ew,h), b=project(xi*.35,-.8,8,eye,ew,h);
                if(a!=null&&b!=null)c.drawLine(x0+a[0],a[1],x0+b[0],b[1],dim);
            }
            double[][] v={{-.45,-.45,2.1},{.45,-.45,2.1},{.45,.45,2.1},{-.45,.45,2.1},{-.45,-.45,3.0},{.45,-.45,3.0},{.45,.45,3.0},{-.45,.45,3.0}};
            float[][] p=new float[8][]; for(int i=0;i<8;i++)p[i]=project(v[i][0],v[i][1],v[i][2],eye,ew,h);
            for(int[] e:edges) if(p[e[0]]!=null&&p[e[1]]!=null)c.drawLine(x0+p[e[0]][0],p[e[0]][1],x0+p[e[1]][0],p[e[1]][1],line);
        }
        private float[] project(double x,double y,double z,double eye,int ew,int h){
            double yr=Math.toRadians(-smoothYaw), pr=Math.toRadians(-smoothPitch);
            x-=eye; double x1=x*Math.cos(yr)-z*Math.sin(yr), z1=x*Math.sin(yr)+z*Math.cos(yr);
            double y1=y*Math.cos(pr)-z1*Math.sin(pr), z2=y*Math.sin(pr)+z1*Math.cos(pr);
            if(z2<.25)return null; double f=ew*.72; return new float[]{(float)(ew/2.0+x1*f/z2),(float)(h/2.0-y1*f/z2)};
        }
        @Override public boolean onTouchEvent(MotionEvent e){ if(e.getAction()==MotionEvent.ACTION_DOWN){downX=e.getX();return true;} if(e.getAction()==MotionEvent.ACTION_UP){ if(Math.abs(e.getX()-downX)<40) recenter(); return true;} return true; }
    }
}

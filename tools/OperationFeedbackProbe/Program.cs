using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FooxClothStudio;
class Program {
[STAThread] static void Main() {
var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
var root=Path.GetFullPath("validation/operation-feedback/"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
var fixture=Path.GetFullPath("validation/hat-preview/hat.fcsproj");
var project=JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
project.Items.Add(new ClothItem { Name="Reservation", ModelPath=project.Items[0].ModelPath, IsDummy=true, Gender="Masculino", Category="Chapéus [p_head]" });
var summary=ProjectOperationSummary.Describe(project);
if(summary!="1 peça(s), 1 textura(s), 1 slot(s) reservado(s)") throw new Exception(summary);
var path=Path.Combine(root,"project.fcsproj"); File.WriteAllText(path,JsonSerializer.Serialize(project));
var owner=new MainWindow {Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};owner.Show();
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
typeof(MainWindow).GetMethod("LoadProject",flags)!.Invoke(owner,[path]);
var save=(Task<bool>)typeof(MainWindow).GetMethod("SaveProjectAsync",flags)!.Invoke(owner,[false])!;
var visible=app.Windows.OfType<OperationProgressWindow>().Single();
var label=((StackPanel)visible.Content).Children.OfType<TextBlock>().Last();
if(!label.Text.Contains("0/1")) throw new Exception("Save total incorrectly includes reserved slots");
void Pump(){var f=new DispatcherFrame();var t=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(30)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
var deadline=DateTime.UtcNow.AddSeconds(30);while(!save.IsCompleted&&DateTime.UtcNow<deadline)Pump();
if(!save.IsCompletedSuccessfully||!save.Result||!owner.IsEnabled||app.Windows.OfType<OperationProgressWindow>().Any()) throw new Exception("Save failed or progress not cleaned up");
var saved=JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(path))!;
if(saved.Items.Count!=2||!File.Exists(Path.Combine(root,saved.Items[0].ModelPath))) throw new Exception("Save roundtrip failed");
var progress=new OperationProgressWindow("Salvando projeto",1,summary) {Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,ShowActivated=false};progress.Show();progress.Report(1,"Chapéu");progress.UpdateLayout();
var bmp=new RenderTargetBitmap((int)progress.ActualWidth,(int)progress.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(progress);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(Path.Combine(root,"save-progress.png")))png.Save(file);progress.Complete();
var build=new BuildProjectWindow(project,p=>p);var blocks=((StackPanel)((ScrollViewer)build.Content).Content).Children.OfType<TextBlock>();if(!blocks.Any(t=>t.Text==summary))throw new Exception("Build summary missing");build.Close();
owner.Close();app.Shutdown();Console.WriteLine("OK: actual save progress counts real pieces, excludes dummy slots, roundtrips assets, closes/re-enables owner; build summary verified. "+root);
}}


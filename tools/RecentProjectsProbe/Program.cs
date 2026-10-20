using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FooxClothStudio;
class Program {
[STAThread] static void Main(){
var root=Path.GetFullPath("validation/recent-projects/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var history=Path.Combine(root,"recent.json");var store=new RecentProjects(history);
for(int i=0;i<12;i++)store.Remember(Path.Combine(root,$"project_{i}.fcsproj"));store.Remember(Path.Combine(root,"project_5.fcsproj"));
var persisted=new RecentProjects(history).Read();if(persisted.Length!=5||Path.GetFileName(persisted[0])!="project_5.fcsproj"||persisted.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=5)throw new Exception("Persistence/order/limit failed");
File.WriteAllText(history,"invalid JSON");if(store.Read().Length!=0)throw new Exception("Corrupt history not handled");
var a=Path.Combine(root,"a.fcsproj");var b=Path.Combine(root,"b.fcsproj");File.WriteAllText(a,JsonSerializer.Serialize(new ClothProject{Name="First"}));File.WriteAllText(b,JsonSerializer.Serialize(new ClothProject{Name="Second"}));
var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());var flags=BindingFlags.Instance|BindingFlags.NonPublic;
MainWindow Window(){var w=new MainWindow{Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};typeof(MainWindow).GetField("_recentProjects",flags)!.SetValue(w,store);w.Show();return w;}
var first=Window();typeof(MainWindow).GetMethod("LoadProject",flags)!.Invoke(first,[a]);typeof(MainWindow).GetMethod("LoadProject",flags)!.Invoke(first,[b]);
var save=(Task<bool>)typeof(MainWindow).GetMethod("SaveProjectAsync",flags)!.Invoke(first,[false])!;
var deadline=DateTime.UtcNow.AddSeconds(20);while(!save.IsCompleted&&DateTime.UtcNow<deadline){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}if(!save.IsCompletedSuccessfully||!save.Result)throw new Exception("Save failed");first.Close();
var second=Window();typeof(MainWindow).GetMethod("RefreshRecentFilesMenu",flags)!.Invoke(second,null);var menu=(MenuItem)second.FindName("RecentFilesMenu");if(menu.Items.Count!=2||((MenuItem)menu.Items[0]).Tag as string!=b)throw new Exception("Recent files did not survive reopening window");
((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));var loaded=(ClothProject)typeof(MainWindow).GetField("_project",flags)!.GetValue(second)!;if(loaded.Name!="First"||store.Read()[0]!=a)throw new Exception("Single-click open or most recent order failed");second.Close();app.Shutdown();Console.WriteLine("OK: persistence, deduplication, 5-item limit, corrupt history, real save/open, new window and menu single-click. "+root);
}}

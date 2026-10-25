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
[STAThread] static void Main(){
var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var root=Path.GetFullPath("validation/duplicate-review/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
var fixture=JsonSerializer.Deserialize<ClothProject>(File.ReadAllText("validation/hat-preview/hat.fcsproj"))!;
foreach(var asset in fixture.Items) {
asset.ModelPath = asset.ModelPath.Replace(@"D:\FooxClothStudio", Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase);
asset.TexturePaths = asset.TexturePaths.Select(p => p.Replace(@"D:\FooxClothStudio", Environment.CurrentDirectory, StringComparison.OrdinalIgnoreCase)).ToList();
}
var item=fixture.Items[0];var original=item.TexturePaths[0];var incoming=Path.Combine(root,"new-texture.ytd");File.Copy(original,incoming);
item.TexturePaths.Add(incoming);item.OriginalTextureFileNames=["old.ytd","new.ytd"];item.TextureLabels=["Old label","New label"];item.TextureEffects=[new(){TimeCycleModifier="old"},new(){TimeCycleModifier="new"}];fixture.ReplaceWithDummyOnRemove=false;
var path=Path.Combine(root,"test.fcsproj");File.WriteAllText(path,JsonSerializer.Serialize(fixture));
var owner=new MainWindow{Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual,ShowInTaskbar=false,ShowActivated=false};owner.Show();
void Pump(){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};t.Tick+=(_,_)=>{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
void Wait(Task task){var deadline=DateTime.UtcNow.AddSeconds(30);while(!task.IsCompleted&&DateTime.UtcNow<deadline)Pump();if(!task.IsCompletedSuccessfully)throw new Exception("Scan failed or timed out");}
ClothProject Project()=>(ClothProject)typeof(MainWindow).GetField("_project",flags)!.GetValue(owner)!;
var exp=(Expander)owner.FindName("DuplicateReview");var pairs=(ComboBox)owner.FindName("DuplicatePairList");var panel=(StackPanel)owner.FindName("DuplicateComparison");
void Load(){typeof(MainWindow).GetMethod("LoadProject",flags)!.Invoke(owner,[path]);exp.IsExpanded=true;Wait((Task)typeof(MainWindow).GetMethod("RefreshDuplicateReviewAsync",flags)!.Invoke(owner,null)!);}
Load();if(pairs.Items.Count!=1||panel.Children.Count!=2)throw new Exception("Old/new comparison missing");
for(var i=0;i<20;i++)Pump();owner.UpdateLayout();
var images=panel.Children.OfType<Border>().Select(b=>(StackPanel)b.Child).SelectMany(s=>s.Children.OfType<WrapPanel>()).SelectMany(w=>w.Children.OfType<StackPanel>()).SelectMany(s=>s.Children.OfType<Border>()).Select(b=>b.Child).OfType<Image>().Count();if(images!=2)throw new Exception("Both YTD previews were not decoded");
var bmp=new RenderTargetBitmap((int)owner.ActualWidth,(int)owner.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(owner);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(root,"review.png")))png.Save(f);
var pair=pairs.SelectedItem!;var old=pair.GetType().GetProperty("Old")!.GetValue(pair)!;
for (var slot = 0; slot < 2; slot++) {
var card = (Border)panel.Children[slot];
var title = (DockPanel)((StackPanel)card.Child).Children[0];
var previewButton = title.Children.OfType<Button>().Single(b => (string)b.Content == "Ver no ped 3D");
previewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
for(var tick=0;tick<20;tick++)Pump();
if (((ListBox)owner.FindName("ItemList")).SelectedItem != Project().Items[0] || ((ListBox)owner.FindName("TextureList")).SelectedIndex != slot) throw new Exception("Review preview selected wrong item/variant");
var previewWindow = (ClothPreviewWindow?)typeof(MainWindow).GetField("_clothPreviewWindow",flags)!.GetValue(owner);
if(previewWindow?.IsVisible != true || !(bool)typeof(MainWindow).GetField("_showBasePed",flags)!.GetValue(owner)!) throw new Exception("Ped preview not opened");
previewWindow.Close();
}
typeof(MainWindow).GetMethod("RemoveReviewedTexture",flags)!.Invoke(owner,[old]);
var remaining=Project().Items[0];if(remaining.TexturePaths.Count!=1||remaining.TexturePaths[0]!=incoming||remaining.TextureLabels[0]!="New label"||remaining.TextureEffects[0].TimeCycleModifier!="new")throw new Exception("Removing old variant lost new variant metadata");if(!File.Exists(original)||!File.Exists(incoming))throw new Exception("Source deleted");
Load();Project().ReplaceWithDummyOnRemove=true;pair=pairs.SelectedItem!;var newer=pair.GetType().GetProperty("New")!.GetValue(pair)!;
typeof(MainWindow).GetMethod("RemoveReviewedTexture",flags)!.Invoke(owner,[newer]);remaining=Project().Items[0];if(remaining.TexturePaths.Count!=2||remaining.TexturePaths[1]!=""||remaining.TextureLabels[1]!="")throw new Exception("Dummy removal behavior broken");
Load();pair=pairs.SelectedItem!;newer=pair.GetType().GetProperty("New")!.GetValue(pair)!;Project().Items[0].TexturePaths[1]=original;
typeof(MainWindow).GetMethod("RemoveReviewedTexture",flags)!.Invoke(owner,[newer]);if(Project().Items[0].TexturePaths.Count!=2)throw new Exception("Stale card removed a different variant");
owner.Close();app.Shutdown();Console.WriteLine("OK: real YTD duplicate grouping, two decoded previews, old/new removal, labels/effects alignment, dummy semantics, source preservation and stale card protection. "+root);
}}

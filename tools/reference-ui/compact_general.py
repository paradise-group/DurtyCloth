from pathlib import Path
p=Path(r'D:\FooxClothStudio\MainWindow.xaml')
s=p.read_text(encoding='utf-8')
s=s.replace('Width="1280" Height="1000"','Width="1280" Height="1030"')
s=s.replace('<Window.Resources>','''<Window.Resources>
        <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#F0F1F2"/></Style>
        <Style TargetType="TabItem"><Setter Property="Foreground" Value="#F0F1F2"/><Setter Property="Background" Value="#363A40"/></Style>
''',1)
a=s.index('                    <TabItem Header="General">')
b=s.index('                    <TabItem Header="Shop Meta">',a)
s=s[:a]+'''                    <TabItem Header="General"><Grid Margin="6">
                        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
                        <Grid.ColumnDefinitions><ColumnDefinition Width="3*"/><ColumnDefinition Width="12"/><ColumnDefinition Width="4*"/></Grid.ColumnDefinitions>
                        <Grid Grid.Row="0" Grid.Column="0" Margin="0,0,0,10">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="95"/><ColumnDefinition Width="8"/><ColumnDefinition/></Grid.ColumnDefinitions>
                            <StackPanel><TextBlock Text="Position"/><TextBox x:Name="PositionBox" Margin="0,4,0,0" LostFocus="Metadata_LostFocus"/></StackPanel>
                            <StackPanel Grid.Column="2"><TextBlock Text="Cloth type"/><ComboBox x:Name="ClothKindBox" Margin="0,4,0,0" SelectionChanged="ClothKind_Changed"><ComboBoxItem Content="Component (Shirt, Pants, Torso etc.)"/><ComboBoxItem Content="Prop (Hat, Glasses etc.)"/></ComboBox></StackPanel>
                        </Grid>
                        <StackPanel Grid.Row="0" Grid.Column="2"><TextBlock Text="Drawable type"/><ComboBox x:Name="TypeBox" Margin="0,4,0,10" SelectionChanged="Details_Changed"/></StackPanel>
                        <Grid Grid.Row="1" Grid.Column="0" Margin="0,0,0,10">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="95"/><ColumnDefinition Width="8"/><ColumnDefinition/></Grid.ColumnDefinitions>
                            <StackPanel><TextBlock Text="Gender"/><ComboBox x:Name="GenderBox" Margin="0,4,0,0" SelectionChanged="Details_Changed"><ComboBoxItem Content="Undefined" Tag="Indefinido"/><ComboBoxItem Content="Male" Tag="Masculino"/><ComboBoxItem Content="Female" Tag="Feminino"/></ComboBox></StackPanel>
                            <StackPanel Grid.Column="2"><TextBlock Text="Name"/><TextBox x:Name="ItemNameBox" Margin="0,4,0,0" LostFocus="ItemNameBox_LostFocus"/></StackPanel>
                        </Grid>
                        <StackPanel Grid.Row="1" Grid.Column="2"><TextBlock Text="First person model"/><DockPanel Margin="0,4,0,10"><Button DockPanel.Dock="Right" Content="..." Click="AddAlternateModel_Click" Margin="0"/><Button DockPanel.Dock="Right" Content="×" Click="RemoveAlternateModel_Click"/><ListBox x:Name="AlternateModelList" Height="25"/></DockPanel></StackPanel>
                        <StackPanel Grid.Row="2" Grid.Column="0"><TextBlock Text="Audio preset"/><TextBox x:Name="AudioBox" Margin="0,4,0,10" LostFocus="Metadata_LostFocus"/></StackPanel>
                        <StackPanel Grid.Row="2" Grid.Column="2"><TextBlock Text="Mesh physics file (.yld)"/><DockPanel Margin="0,4,0,10"><Button DockPanel.Dock="Right" Content="..." Click="SetPhysics_Click" Margin="0"/><Button DockPanel.Dock="Right" Content="×" Click="RemovePhysics_Click"/><TextBlock x:Name="PhysicsText" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/></DockPanel></StackPanel>
                        <Grid Grid.Row="3" Grid.ColumnSpan="3">
                            <Grid.ColumnDefinitions><ColumnDefinition Width="3*"/><ColumnDefinition Width="12"/><ColumnDefinition Width="1*"/></Grid.ColumnDefinitions>
                            <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                                <TextBlock x:Name="TexturesHeading" Text="Textures 0/26" Margin="0,0,0,5"/>
                                <ListBox x:Name="TextureList" Grid.Row="1" Height="560" SelectionChanged="TextureList_SelectionChanged"/>
                                <WrapPanel Grid.Row="2" Margin="0,5,0,0"><Button Content="Add" Click="AddTexture_Click"/><Button Content="Remove selected" Click="RemoveTexture_Click"/><Button Content="▲" Click="MoveTextureUp_Click"/><Button Content="▼" Click="MoveTextureDown_Click"/><Button Content="3D Preview Cloth" Click="Preview_Click"/></WrapPanel>
                            </Grid>
                            <StackPanel Grid.Column="2"><TextBlock Text="Options" Margin="0,0,0,12"/><TextBlock Text="Behaviour Flags"/><TextBox x:Name="BehaviourBox" Margin="0,4,0,12" LostFocus="Metadata_LostFocus" ToolTip="Default or raw propMask (0–255)"/></StackPanel>
                        </Grid>
                    </Grid></TabItem>
'''+s[b:]
s=s.replace('<TabItem x:Name="PreviewTab" Header="3D Preview Cloth"><StackPanel Margin="6">','''<TabItem x:Name="PreviewTab" Header="3D Preview Cloth"><StackPanel Margin="6">
                    <TextBlock Text="Main model"/><TextBlock x:Name="ModelText" TextWrapping="Wrap"/><TextBlock x:Name="ModelInfoText" TextWrapping="Wrap"/>
                    <WrapPanel Margin="0,6,0,12"><Button Content="Select main model" Click="ReplaceModel_Click"/><Button Content="Replace texture" Click="ReplaceTexture_Click"/><Button Content="Export native files" Click="ExportItemFiles_Click"/><Button Content="Move alternate up" Click="MoveAlternateModelUp_Click"/><Button Content="Move alternate down" Click="MoveAlternateModelDown_Click"/></WrapPanel>
''')
p.write_text(s,encoding='utf-8')

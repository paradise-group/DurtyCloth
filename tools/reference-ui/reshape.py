from pathlib import Path
p=Path(r'D:\FooxClothStudio\MainWindow.xaml')
s=p.read_text(encoding='utf-8-sig')
s=s.replace('Width="1280" Height="800"','Width="1280" Height="1000"').replace('FontSize="13"','FontSize="12"',1)
s=s.replace('Value="12,7"','Value="6,3"')
s=s.replace('<RowDefinition Height="94"/>','<RowDefinition Height="28"/>')
a=s.index('        <Border Grid.Row="0"')
b=s.index('        <Grid Grid.Row="1"',a)
s=s[:a]+'''        <Menu Grid.Row="0" Background="#292D31" Foreground="#F0F1F2">
            <MenuItem Header="Project">
                <MenuItem Header="New project" Click="New_Click"/>
                <MenuItem Header="Open project" Click="Open_Click"/>
                <MenuItem Header="Save" Click="Save_Click"/>
                <MenuItem Header="Save as" Click="SaveAs_Click"/>
                <Separator/>
                <MenuItem Header="Import package" Click="ImportBundle_Click"/>
                <MenuItem Header="Export project" Click="ExportBundle_Click"/>
                <MenuItem Header="Open FiveM resource" Click="OpenResource_Click"/>
                <MenuItem Header="Copy resource" Click="CopyResource_Click"/>
            </MenuItem>
            <MenuItem Header="Options">
                <MenuItem Header="Add" Click="ImportFiles_Click"/>
                <MenuItem Header="Add folder" Click="ImportFolder_Click"/>
                <MenuItem Header="Export selected drawable" Click="ExportItemFiles_Click"/>
                <MenuItem Header="Export selected package" Click="ExportItemBundle_Click"/>
                <MenuItem Header="Export stream" Click="Export_Click"/>
            </MenuItem>
            <MenuItem Header="View"><MenuItem Header="Error List" Click="Validate_Click"/></MenuItem>
            <MenuItem Header="Help" Click="Help_Click"/>
        </Menu>
'''+s[b:]
a=s.index('            <Grid.ColumnDefinitions>')
b=s.index('            <Border Grid.Column="2"',a)
s=s[:a]+'''            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="33*"/>
                <ColumnDefinition Width="6"/>
                <ColumnDefinition Width="67*"/>
            </Grid.ColumnDefinitions>
'''+s[b:]
s=s.replace('<Border Grid.Column="2"','<Border Grid.Column="0"',1).replace('<Border Grid.Column="4"','<Border Grid.Column="2"',1)
s=s.replace('<RowDefinition Height="52"/>','<RowDefinition Height="80"/>',1)
a=s.index('                    <DockPanel Grid.Row="0"')
b=s.index('                    <ListBox x:Name="ItemList"',a)
s=s[:a]+'''                    <StackPanel Grid.Row="0" Margin="6">
                        <TextBlock Text="Drawables" Margin="0,0,0,4"/>
                        <TextBox x:Name="SearchBox" TextChanged="Filter_Changed" ToolTip="Enter search term (CTRL+F)"/>
                        <ComboBox x:Name="CategoryList" SelectionChanged="Filter_Changed" Margin="0,4,0,0"/>
                    </StackPanel>
'''+s[b:]
s=s.replace('Padding="9"','Padding="3"')
s=s.replace('Content="Importar peça"','Content="Import package"').replace('Content="Exportar peça" Click="ExportItemBundle_Click"','Content="Add" Click="ImportFiles_Click"').replace('Content="Remover" Click="Remove_Click"','Content="Remove" Click="Remove_Click"')
s=s.replace('Text="DETALHES"','Text="Edit selected drawable"')
# Keep live controls and preview handlers; group supplemental inspection into a tab.
a=s.index('                    <TextBlock Text="Nome"')
b=s.index('                    <TextBlock Text="Modelo"',a)
general='''                    <Grid Margin="0,0,0,10">
                        <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width="12"/><ColumnDefinition/></Grid.ColumnDefinitions>
                        <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                        <StackPanel Grid.Column="0"><TextBlock Text="Gender"/><ComboBox x:Name="GenderBox" Margin="0,4,0,8" SelectionChanged="Details_Changed"><ComboBoxItem Content="Indefinido"/><ComboBoxItem Content="Masculino"/><ComboBoxItem Content="Feminino"/></ComboBox></StackPanel>
                        <StackPanel Grid.Column="2"><TextBlock Text="Drawable type"/><ComboBox x:Name="TypeBox" Margin="0,4,0,8" SelectionChanged="Details_Changed"/></StackPanel>
                        <StackPanel Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"><TextBlock Text="Name"/><TextBox x:Name="ItemNameBox" Margin="0,4,0,8" LostFocus="ItemNameBox_LostFocus"/></StackPanel>
                    </Grid>
'''
s=s[:a]+'''                    <TabControl Background="#25282C" Foreground="#F0F1F2">
                    <TabItem Header="General"><StackPanel Margin="6">
'''+general+s[b:]
a=s.index('                    <TextBlock Text="Prévia geométrica 3D"')
s=s[:a]+'''                    </StackPanel></TabItem>
                    <TabItem Header="3D Preview Cloth"><StackPanel Margin="6">
'''+s[a:]
a=s.index('                </StackPanel>\n                </ScrollViewer>')
s=s[:a]+'''                    </StackPanel></TabItem>
                    </TabControl>
'''+s[a:]
mapping={'Modelo':'Main model','Modelos YDD alternativos':'First person / alternate models','Física YLD':'Mesh physics file (.yld)','Variantes YTD da peça':'Textures','Adicionar YTD':'Add','Substituir':'Replace','Remover':'Remove','Substituir YDD principal':'Select main model','Adicionar YDD':'Add model','Associar YLD':'Select a file','Remover YLD':'Remove physics','Exportar YDD/YTD/YLD desta peça':'Export native files'}
for x,y in mapping.items():
 s=s.replace('Text="'+x+'"','Text="'+y+'"').replace('Content="'+x+'"','Content="'+y+'"')
a=s.index('            <DockPanel Margin="12,0">')
b=s.index('            </DockPanel>',a)+len('            </DockPanel>')
s=s[:a]+'''            <DockPanel Margin="8,0">
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Content="Error List" Click="Validate_Click"/>
                    <Button Content="Build project" Click="ExportAddon_Click" Background="#B9413D" Margin="0"/>
                </StackPanel>
                <StackPanel VerticalAlignment="Center">
                    <TextBlock x:Name="ProjectText" Foreground="#B7BDC4" FontSize="10"/>
                    <TextBlock x:Name="StatusText" FontSize="10"/>
                </StackPanel>
            </DockPanel>'''+s[b:]
p.write_text(s,encoding='utf-8')

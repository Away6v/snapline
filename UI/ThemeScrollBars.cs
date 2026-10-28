using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace SnapLine;

internal static class ThemeScrollBars
{
    internal static void Apply(Application application)
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                   TargetType="{x:Type ScrollBar}">
                <Setter Property="Width" Value="10" />
                <Setter Property="Background" Value="Transparent" />
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="{x:Type ScrollBar}">
                            <Border Background="{TemplateBinding Background}" CornerRadius="6">
                                <Track x:Name="PART_Track" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True">
                                    <Track.DecreaseRepeatButton>
                                        <RepeatButton x:Name="Decrease" Command="{x:Static ScrollBar.PageUpCommand}" Focusable="False">
                                            <RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent" /></ControlTemplate></RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.DecreaseRepeatButton>
                                    <Track.Thumb>
                                        <Thumb x:Name="ScrollThumb" MinHeight="30" Focusable="False">
                                            <Thumb.Template>
                                                <ControlTemplate TargetType="{x:Type Thumb}">
                                                    <Border x:Name="ThumbSurface" Background="#B7CBD9" CornerRadius="2" Margin="3" />
                                                    <ControlTemplate.Triggers>
                                                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ThumbSurface" Property="Background" Value="#78B9DF" /></Trigger>
                                                        <Trigger Property="IsDragging" Value="True"><Setter TargetName="ThumbSurface" Property="Background" Value="#38A9E8" /></Trigger>
                                                    </ControlTemplate.Triggers>
                                                </ControlTemplate>
                                            </Thumb.Template>
                                        </Thumb>
                                    </Track.Thumb>
                                    <Track.IncreaseRepeatButton>
                                        <RepeatButton x:Name="Increase" Command="{x:Static ScrollBar.PageDownCommand}" Focusable="False">
                                            <RepeatButton.Template><ControlTemplate TargetType="{x:Type RepeatButton}"><Border Background="Transparent" /></ControlTemplate></RepeatButton.Template>
                                        </RepeatButton>
                                    </Track.IncreaseRepeatButton>
                                </Track>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="Orientation" Value="Horizontal">
                                    <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False" />
                                    <Setter TargetName="ScrollThumb" Property="MinHeight" Value="0" />
                                    <Setter TargetName="ScrollThumb" Property="MinWidth" Value="30" />
                                    <Setter TargetName="Decrease" Property="Command" Value="{x:Static ScrollBar.PageLeftCommand}" />
                                    <Setter TargetName="Increase" Property="Command" Value="{x:Static ScrollBar.PageRightCommand}" />
                                </Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
                <Style.Triggers>
                    <Trigger Property="Orientation" Value="Horizontal">
                        <Setter Property="Width" Value="Auto" />
                        <Setter Property="Height" Value="10" />
                    </Trigger>
                </Style.Triggers>
            </Style>
            """;
        application.Resources[typeof(ScrollBar)] = (Style)XamlReader.Parse(xaml);
        // 默认滚动容器会显式引用系统宽度，因此同步覆盖尺寸资源，确保窄滑块实际生效。
        application.Resources[SystemParameters.VerticalScrollBarWidthKey] = 10.0;
        application.Resources[SystemParameters.HorizontalScrollBarHeightKey] = 10.0;
    }
}

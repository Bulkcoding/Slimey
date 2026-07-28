using System.Windows;
using ThrowMe.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace ThrowMe.Views.Skins;

public partial class JellySkin : UserControl, ISkinExpressions
{
    public JellySkin() => InitializeComponent();

    public void SetExpression(SlimeExpression expression)
    {
        ExprNormal.Visibility = expression == SlimeExpression.Normal ? Visibility.Visible : Visibility.Collapsed;
        ExprFlying.Visibility = expression == SlimeExpression.Flying ? Visibility.Visible : Visibility.Collapsed;
        ExprDizzy.Visibility = expression == SlimeExpression.Dizzy ? Visibility.Visible : Visibility.Collapsed;
    }
}

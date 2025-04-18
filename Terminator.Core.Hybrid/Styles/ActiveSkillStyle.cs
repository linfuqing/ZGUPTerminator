
public class ActiveSkillStyle : SkillStyle
{
    public ZG.UI.Progressbar cooldown;
    public ActiveSkillStyle childStyle;
    
    public ActiveSkillStyle GetChild(int level)
    {
        switch (level)
        {
            case 0:
                return this;
            case 1:
                return childStyle;
            default:
                if (childStyle == null)
                    return null;

                return childStyle.GetChild(level - 1);
        }
    }
}

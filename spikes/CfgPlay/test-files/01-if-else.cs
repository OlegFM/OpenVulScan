public class TestIfElse
{
    private int _threshold = 10;

    public int SimpleIfElse(int x)
    {
        if (x > 0)
        {
            return 1;
        }
        else
        {
            return -1;
        }
    }

    public int InstanceMethod(int x)
    {
        if (x > _threshold)
        {
            return _threshold;
        }
        return x;
    }
}

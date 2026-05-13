public class TestLoops
{
    public int ForLoop(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += i;
        }
        return sum;
    }

    public int WhileLoop(int n)
    {
        int sum = 0;
        int i = 0;
        while (i < n)
        {
            sum += i;
            i++;
        }
        return sum;
    }
}

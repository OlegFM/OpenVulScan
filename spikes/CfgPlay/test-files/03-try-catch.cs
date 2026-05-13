public class TestTryCatch
{
    public int TryCatchFinally(string input)
    {
        int result = 0;
        try
        {
            result = int.Parse(input);
        }
        catch (FormatException ex)
        {
            result = -1;
        }
        catch (Exception ex)
        {
            result = -2;
        }
        finally
        {
            result = result * 2;
        }
        return result;
    }
}

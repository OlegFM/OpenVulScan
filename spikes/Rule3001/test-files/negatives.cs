public class Negatives
{
    // 1. Assignment is explicitly excluded
    public int N1(int a)
    {
        a = a;
        return a;
    }

    // 2. Different operands
    public int N2(int a, int b) => a + b;

    // 3. Commutative but structurally different: (a + b) vs (b + a)
    public int N3(int a, int b) => (a + b) * (b + a);
}

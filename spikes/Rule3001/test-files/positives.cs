public class Positives
{
    // 1. Simple equality comparison
    public bool M1(int a) => a == a;

    // 2. Arithmetic addition
    public int M2(int a) => a + a;

    // 3. Nested expressions: (a + b) * (a + b)
    public int M3(int a, int b) => (a + b) * (a + b);

    // 4. Logical AND
    public bool M4(bool a) => a && a;

    // 5. Bitwise OR
    public int M5(int a) => a | a;
}

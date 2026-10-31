using System.Globalization;

namespace Hypo.Core.Util;

/// <summary>极小的数学表达式求值：+ - * / ^ () 、变量、ceil/floor/round/min/max。</summary>
public static class Formula
{
    public static double Eval(string expr, IDictionary<string, double> vars)
    {
        var p = new P(expr.Replace(" ", ""), vars);
        var v = p.Expr();
        if (p.i < p.s.Length) throw new FormatException("公式多余字符: " + expr[p.i..]);
        return v;
    }
    public static int EvalCeil(string expr, IDictionary<string, double> vars)
    {
        try { return (int)Math.Ceiling(Eval(expr, vars) - 1e-9); }
        catch (Exception e) { Data.Log.Warn("公式错误 " + expr + ": " + e.Message); return 0; }
    }

    class P
    {
        public string s; public int i; readonly IDictionary<string, double> v;
        public P(string s, IDictionary<string, double> v) { this.s = s; this.v = v; }
        char C => i < s.Length ? s[i] : '\0';
        public double Expr()
        {
            var x = Term();
            while (C is '+' or '-') { var o = s[i++]; var y = Term(); x = o == '+' ? x + y : x - y; }
            return x;
        }
        double Term()
        {
            var x = Pow();
            while (C is '*' or '/') { var o = s[i++]; var y = Pow(); x = o == '*' ? x * y : x / y; }
            return x;
        }
        double Pow()
        {
            var x = Unary();
            if (C == '^') { i++; x = Math.Pow(x, Pow()); }
            return x;
        }
        double Unary() { if (C == '-') { i++; return -Unary(); } return Atom(); }
        double Atom()
        {
            if (C == '(') { i++; var x = Expr(); i++; return x; }
            int st = i;
            if (char.IsDigit(C) || C == '.')
            {
                while (char.IsDigit(C) || C == '.') i++;
                return double.Parse(s[st..i], CultureInfo.InvariantCulture);
            }
            while (char.IsLetterOrDigit(C) || C == '_') i++;
            var name = s[st..i];
            if (C == '(')
            {
                i++; var args = new List<double> { Expr() };
                while (C == ',') { i++; args.Add(Expr()); }
                i++;
                return name switch
                {
                    "ceil" => Math.Ceiling(args[0]), "floor" => Math.Floor(args[0]),
                    "round" => Math.Round(args[0]), "min" => Math.Min(args[0], args[1]),
                    "max" => Math.Max(args[0], args[1]), "abs" => Math.Abs(args[0]),
                    _ => throw new FormatException("未知函数 " + name)
                };
            }
            return v.TryGetValue(name, out var val) ? val : throw new FormatException("未知变量 " + name);
        }
    }
}

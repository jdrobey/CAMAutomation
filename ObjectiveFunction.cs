using System;
using System.Collections.Generic;
using System.Linq;


namespace CAMAutomation
{
    public class ObjectiveFunction<T>: Utilities.FunctionCompiler<T>
    {
        public ObjectiveFunction(SortedList<string, Func<T, object>> inputLambdas, string stringFunctionOutput, string sourceCodeDelegates = "", bool isNormalizeRequested = false) : 
            base(inputLambdas, stringFunctionOutput, sourceCodeDelegates)
        {
            IsNormalizeRequested = isNormalizeRequested;
        }


        ~ObjectiveFunction()
        {
            // empty
        }


        public T[] FilterNonFiniteLambdaOutput(T[] array)
        {
            return array.Where(p => HasFiniteLambdaOutput(p)).ToArray();
        }


        public T[] Sort(T[] array, params Func<T, double>[] thenByDescending)
        {
            return SortTuple(array, thenByDescending).Select(p => p.Item1).ToArray();
        }


        public Tuple<T, double>[] SortTuple(T[] array, params Func<T, double>[] thenByDescending)
        {
            // Sort according to objective function normalized if requested
            Utilities.ToleranceDouble compareDouble = new Utilities.ToleranceDouble();
            SortedList<string, Func<T, object>> normalize = Normalize(array);
            IOrderedEnumerable<Tuple<T, double>> sortArray = array.Select(p => new Tuple<T, double>(p, (double)Apply(p, normalize)))
                                                                  .OrderByDescending(p => p.Item2, compareDouble);

            foreach (Func<T, double> f in thenByDescending)
            {
                sortArray = sortArray.ThenByDescending(p => f(p.Item1), compareDouble);
            }

            return sortArray.ToArray();
        }


        private bool HasFiniteLambdaOutput(T c)
        {
            return !InputLambdas.Values.Select(f => f(c)).Where(n => IsNumericType(n)).Any(p => double.IsNaN(Convert.ToDouble(p)) || double.IsInfinity(Convert.ToDouble(p)));
        }


        private SortedList<string, Func<T, object>> Normalize(T[] cc)
        {
            if (!IsNormalizeRequested || cc.Length == 0)
            {
                return InputLambdas;
            }
            else
            {
                UpdateNormalisationsValues(cc);
            }
            
            return NormalizedInputLambdas;
        }


        private void UpdateNormalisationsValues(T[] cc)
        {
            // Convert all numerical value of InputLambdas to double for Normalisation 
            // This has to be done only once and is triger by app.config option
            if (NormalizedInputLambdas == null)
            {
                ConvertInputLambdasToDouble(cc);
            }

            // Create a Normalyzation for InputLambdas function of Numeric Type every Sort call to update the normalisation
            // Non-Numeric Type keep same InputLambdas
            NormalizedInputLambdas = new SortedList<string, Func<T, object>>(InputLambdas);
            foreach (string key in NormalizedInputLambdas.Keys.ToArray())
            {
                Func<T, object> Value = NormalizedInputLambdas[key];
                if (IsNumericType(Value(cc.First())))
                {
                    List<double> values = cc.Select((c) => Convert.ToDouble(Value(c))).ToList();
                    double span = Math.Max(values.Max() - values.Min(), Utilities.MathUtils.ABS_TOL);
                    bool noSpan = span == Utilities.MathUtils.ABS_TOL;

                    NormalizedInputLambdas[key] = (c) => (noSpan) ? 1.0 : (Convert.ToDouble(Value(c)) - values.Min()) / span;
                }
            }
        }


        private void ConvertInputLambdasToDouble(T[] cc)
        {
            foreach (string key in InputLambdas.Keys.ToArray())
            {
                Func<T, object> Value = InputLambdas[key];
                if (IsNumericType(Value(cc.First())))
                {
                    InputLambdas[key] = (c) => Convert.ToDouble(Value(c));
                }
            }
        }


        private bool IsNumericType(object o)
        {
            switch (Type.GetTypeCode(o.GetType()))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }


        public bool IsNormalizeRequested { get; }
        public SortedList<string, Func<T, object>> NormalizedInputLambdas { get; private set; }
    }
}

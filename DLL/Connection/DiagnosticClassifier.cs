using System.Diagnostics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.LinearAlgebra;

namespace DeviceInterface.Connection
{
    /// <summary>
    ///     Embodies the trained Classifier for detection of whether the "patient"
    ///     is in a Seizure state
    /// </summary>
    internal class DiagnosticClassifier
    {
        public const int CLASSIFIER_WINDOW_DATAPOINTS = 178;
        public const int CLASSIFIER_WINDOW_MS = 1000;

        private readonly double[] W = {
                4.72637723538132e-05, 0.00010010973711889297, 5.928329840460225e-05,
                3.6146288914730934e-05, 5.566774502577034e-05, 0.00013062425507188045,
                0.00010589644516270762, 4.783655072387502e-05, 2.63687355546717e-05,
                4.260057912688773e-05, -5.743788184124312e-06, 1.9093552293351764e-05,
                4.927277853014348e-05, 3.136843253917799e-05, 2.7044335945572443e-06,
                -1.9644571424177843e-05, -8.155627386749212e-05, -0.00010480761221720177,
                2.4114119783525807e-05, 3.8817393901795145e-05, 2.930142227430835e-05,
                -7.414936604746609e-06, -3.410644822808694e-05, -5.108654151486227e-05,
                0.00010801359073416009, 0.00026313691166818124, 0.00022739840596116244,
                6.251751272065879e-05, 9.409650340377488e-05, -3.6773785359353093e-05,
                3.8272629488959245e-05, -8.096059261865753e-06, 0.0005078526318549762,
                8.350907377693948e-05, 0.0001678392598434375, 0.0005900508829965582,
                -5.718594361287379e-05, 0.00017061853995155817, 0.0006102112818037206,
                -0.0001839686306759241, 0.00013863016234835003, -0.00042004991980962726,
                -6.592718644136127e-05, -0.0007307904823559211, -0.0009147328982019268
            };

        private const double B = -4.107084483430048;
        private readonly VectorBuilder<double> V = Vector<double>.Build;

        /// <summary>
        ///     Apply the classifier, to determine whether the provided sample represents
        ///     a seizure or normal brain activity.
        /// </summary>
        /// <param name="sample">The provided sample, which must contain
        ///     <see cref="CLASSIFIER_WINDOW_DATAPOINTS"/> values.
        /// </param>
        /// <returns>
        ///     An instance of <see cref="SeizureStatusClassification"/>, containing the
        ///     determination, plus additional information about the calculation.
        /// </returns>
        internal SeizureStatusClassification Classify(double[] sample)
        {
            Debug.Assert(CLASSIFIER_WINDOW_DATAPOINTS == sample.Length);

            var imaginary = new double[CLASSIFIER_WINDOW_DATAPOINTS];
            Fourier.Forward(sample, imaginary, FourierOptions.NoScaling);
            var spectralPower = new double[W.Length];
            var determinant = B;
            for (var i = 0; i < W.Length; i++)
            {
                // Note: skipping the DC component of FFT result, because that's how I trained it
                spectralPower[i] = Math.Sqrt(sample[i + 1] * sample[i + 1] + imaginary[i + 1] * imaginary[i + 1]);
                determinant += W[i] * spectralPower[i];
            }

            return new SeizureStatusClassification(determinant > 0, (float)Math.Abs(determinant), spectralPower);
        }

    }
}

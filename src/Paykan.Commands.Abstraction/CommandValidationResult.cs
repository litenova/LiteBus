namespace Paykan.Commands.Abstraction
{
    public class CommandValidationResult
    {
        /// <summary>
        /// Constructor that accepts an error message.
        /// </summary>
        /// <param name="errorMessage">The user-visible error message.</param>
        public CommandValidationResult(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        #region Properties

        /// <summary>
        /// Gets the error message for this result.  It may be null.
        /// </summary>
        public string ErrorMessage { get; }

        #endregion

        /// <summary>
        /// Override the string representation of this instance, returning
        /// the <see cref="ErrorMessage"/> if not <c>null</c>, otherwise
        /// the base <see cref="object.ToString"/> result.
        /// </summary>
        /// <remarks>
        /// If the <see cref="ErrorMessage"/> is empty, it will still qualify
        /// as being specified, and therefore returned from <see cref="ToString"/>.
        /// </remarks>
        /// <returns>The <see cref="ErrorMessage"/> property value if specified,
        /// otherwise, the base <see cref="object.ToString"/> result.</returns>
        public override string ToString()
        {
            return this.ErrorMessage ?? base.ToString();
        }
    }
}
namespace PaperlessMCP.Utils;

/// <summary>
/// Validation for the document <c>title</c> field against the limit Paperless-ngx
/// actually enforces on ingestion.
/// </summary>
/// <remarks>
/// <para>
/// The Django model declares <c>title = models.CharField(max_length=128)</c>, but the
/// effective limit is <b>127</b>, and it differs per write path:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>Upload</b> (<c>POST /api/documents/post_document/</c>): <c>PostDocumentSerializer.title</c>
/// is a plain <c>CharField</c> with no <c>max_length</c>, so DRF runs no length validation.
/// The consumer then stores <c>Document.objects.create(title=title[:127])</c>
/// (<c>src/documents/consumer.py</c>). A longer title is therefore <b>silently truncated to 127</b>
/// and the API still reports success — the truncation also propagates to
/// <c>archived_file_name</c>.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>Update</b> (<c>PATCH /api/documents/{id}/</c>): <c>DocumentSerializer</c> is a
/// ModelSerializer, so DRF does generate <c>MaxLengthValidator(128)</c> and rejects with
/// HTTP 400 above 128. No silent truncation here.
/// </description>
/// </item>
/// </list>
/// <para>
/// Consequence: a title of exactly 128 characters passes the update path but is still
/// truncated to 127 on upload. We therefore validate every write path against <b>127</b>,
/// the only length that is safe everywhere, and reject rather than truncate: a rejected
/// call is cheap to retry, whereas a mutilated title is permanent and silent.
/// </para>
/// <para>Verified against Paperless-ngx 2.20.15.</para>
/// </remarks>
public static class TitleValidation
{
    /// <summary>
    /// Maximum number of characters Paperless-ngx stores for a document title without
    /// truncating it. Lower than the model's <c>max_length=128</c> because the consumer
    /// slices the title with <c>[:127]</c> on ingestion.
    /// </summary>
    public const int MaxTitleLength = 127;

    /// <summary>
    /// Checks a document title against <see cref="MaxTitleLength"/>.
    /// </summary>
    /// <param name="title">
    /// The effective title that would be sent to Paperless. May be <c>null</c>, which is
    /// valid: Paperless derives the title from the filename in that case.
    /// </param>
    /// <param name="errorMessage">
    /// A message naming the limit and the actual length, or <c>null</c> when the title is valid.
    /// </param>
    /// <returns><c>true</c> when the title is safe to send; otherwise <c>false</c>.</returns>
    public static bool IsValid(string? title, out string? errorMessage)
    {
        if (title is null || title.Length <= MaxTitleLength)
        {
            errorMessage = null;
            return true;
        }

        errorMessage =
            $"Title is {title.Length} characters; Paperless-ngx stores at most {MaxTitleLength}. " +
            "Longer titles are silently truncated on upload (the model allows 128, but the " +
            "consumer stores title[:127]), which would corrupt the title and the archived " +
            "file name. Shorten the title to " + MaxTitleLength + " characters or fewer and retry.";
        return false;
    }
}

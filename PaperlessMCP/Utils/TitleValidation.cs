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
/// <c>archived_file_name</c>. When no title is supplied at all, Paperless derives one from
/// the uploaded file's stem and truncates it just the same, so callers must validate the
/// <i>effective</i> title (explicit title, or filename stem when the title is null/empty).
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
/// truncated to 127 on upload. <b>Policy (intentional): every write path validates against
/// 127, including update.</b> PATCH would store 128 intact, but accepting 128 there while
/// upload truncates it would make the same title valid or corrupted depending on which tool
/// wrote it; we trade that one character of PATCH capacity for a uniform contract. And we
/// reject rather than truncate: a rejected call is cheap to retry, whereas a mutilated
/// title is permanent and silent.
/// </para>
/// <para>
/// <b>Counting semantics:</b> lengths are measured in Unicode code points
/// (<see cref="System.Text.Rune"/>), not UTF-16 code units, because Paperless's
/// <c>title[:127]</c> is a Python string slice and Python strings are sequences of code
/// points. A title of 64 astral-plane emoji is 128 UTF-16 code units but only 64 code
/// points — Paperless stores it intact, so it must validate as intact here.
/// </para>
/// <para>
/// <b>Scope:</b> this guard covers literal titles and the filename fallback. Paperless
/// expands title placeholders (e.g. <c>{correspondent}</c>) <i>before</i> applying
/// <c>title[:127]</c>, so a short template title can still exceed the limit after
/// server-side expansion; template expansion is out of scope here.
/// </para>
/// <para>Verified against Paperless-ngx 2.20.15.</para>
/// </remarks>
public static class TitleValidation
{
    /// <summary>
    /// Maximum number of Unicode code points Paperless-ngx stores for a document title
    /// without truncating it. Lower than the model's <c>max_length=128</c> because the
    /// consumer slices the title with <c>[:127]</c> on ingestion.
    /// </summary>
    public const int MaxTitleLength = 127;

    /// <summary>
    /// Checks a document title against <see cref="MaxTitleLength"/>.
    /// </summary>
    /// <param name="title">
    /// The effective title that would be stored by Paperless: the explicit title, or the
    /// filename stem when no title is supplied. May be <c>null</c>, which is valid (callers
    /// that have a filename fallback should pass the fallback instead).
    /// </param>
    /// <param name="errorMessage">
    /// A message naming the limit and the actual length, or <c>null</c> when the title is valid.
    /// </param>
    /// <returns><c>true</c> when the title is safe to send; otherwise <c>false</c>.</returns>
    public static bool IsValid(string? title, out string? errorMessage)
    {
        if (title is null)
        {
            errorMessage = null;
            return true;
        }

        // Count Unicode code points, not UTF-16 code units: Paperless truncates with a
        // Python slice, and Python slices count code points. See class remarks.
        var length = title.EnumerateRunes().Count();

        if (length <= MaxTitleLength)
        {
            errorMessage = null;
            return true;
        }

        errorMessage =
            $"Title is {length} characters; Paperless-ngx stores at most {MaxTitleLength}. " +
            "Longer titles are silently truncated on upload (the model allows 128, but the " +
            "consumer stores title[:127]), which would corrupt the title and the archived " +
            "file name. Shorten the title to " + MaxTitleLength + " characters or fewer and retry.";
        return false;
    }

    /// <summary>
    /// Derives the title Paperless-ngx would store for an upload that carries no explicit
    /// title, by applying Python's <c>pathlib</c> stem semantics to the uploaded file name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The consumer does <c>title = Path(filename).stem</c> and then <c>title[:127]</c>
    /// (<c>src/documents/consumer.py</c>), so the fallback title is whatever <c>pathlib</c>
    /// calls the stem — which is <b>not</b> what
    /// <see cref="Path.GetFileNameWithoutExtension(string)"/> returns.
    /// </para>
    /// <para>
    /// Python only treats the last <c>'.'</c> as an extension separator when it is neither
    /// the first nor the last character of the name. Two cases diverge from .NET, and both
    /// hide over-long titles from validation:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Dotfiles.</b> <c>Path(".bashrc").stem</c> is <c>".bashrc"</c>, but
    /// <c>Path.GetFileNameWithoutExtension(".bashrc")</c> is the empty string. A 200-character
    /// dotfile uploaded without a title would look like an empty (valid) title here and be
    /// truncated to 127 server-side.
    /// </description></item>
    /// <item><description>
    /// <b>Trailing dot.</b> <c>Path("report.").stem</c> is <c>"report."</c>, while .NET drops
    /// the dot and returns <c>"report"</c> — one character short, enough to let a name of
    /// exactly 128 characters slip through.
    /// </description></item>
    /// </list>
    /// <para>
    /// Directory separators are split on <c>'/'</c> only, matching <c>PurePosixPath</c>:
    /// Paperless-ngx runs on Linux, so a backslash inside an uploaded file name is an
    /// ordinary character there regardless of the host running this server.
    /// </para>
    /// </remarks>
    /// <param name="fileName">The file name as it will be sent to Paperless-ngx.</param>
    /// <returns>The stem Paperless would use as the title; empty when there is no name.</returns>
    public static string GetFallbackTitle(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        // Path("a/b/").name == "b": pathlib ignores trailing separators.
        var trimmed = fileName.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        var name = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;

        // pathlib: suffix exists only when 0 < index < len(name) - 1.
        var lastDot = name.LastIndexOf('.');
        return lastDot > 0 && lastDot < name.Length - 1 ? name[..lastDot] : name;
    }
}

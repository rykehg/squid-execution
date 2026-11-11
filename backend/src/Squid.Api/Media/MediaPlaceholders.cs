namespace Squid.Api.Media;

/// <summary>
/// SPEC-0009 section J (GAP-21): the registered placeholder every piece of media without a
/// generated thumbnail serves — video and audio never decode into pixels, so they keep this
/// static tile instead, and an image whose Worker pass has not run yet degrades to it rather
/// than to a 404. One definition: the Worker registers these very bytes for a video/audio blob
/// and the serving route falls back to them, so both paths answer the same image.
/// </summary>
public static class MediaPlaceholders
{
    /// <summary>The placeholder tile (256×144, a play glyph on a dark field), embedded as base64.</summary>
    public static readonly byte[] Bytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAQAAAACQCAYAAADnY7WRAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAP1SURBVHhe7ddBbl1FFEVRRpQWA0mboWUATBMEEghtkshJ7Pzve1ZjuWH5VVVnH8m/fPjw6x/Apl/++vHx42/AGAMAwwwADPvsAPR/BOCOtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugGAIW3dAMCQtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugGAIW3dAMCQtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugGAIW3dAMCQtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugGAIW3dAMCQtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugGAIW3dAMCQtm4AYEhbNwAwpK0bABjS1g0ADGnrBgCGtHUDAEPaugE44tOn3//W38N/tXUDcMQ/A2AI+Jq2bgCO6AAYAj6nrRuAIxp+9e/Z1NYNwBEN/kv6HVvaugE4oqF/Tb9lR1s3AEc08pfoGdzX1g3AEY37W/Qs7mrrBuCIRv09eib3tHUDcERj/hE9mzvaugE4ohH/qJ7PDW3dABzRgF9L7+F9a+sG4IiG+9p6H+9TWzcARzTYt9J7eV/augE4oqG+td7P+9DWDcARDfRn6Tt4bm3dABzRMH+mvoXn1dYNwBGN8hH6Jp5PWzcARzTGR+rbeB5t3QAc0QifQd/I47V1A3BE43smfSuP09YNwBGN7tn0vTxGWzcARzS4Z9K38jht3QAc0eieQd/I47V1A3BE43ukvo3n0dYNwBGN8BH6Jp5PWzcARzTGn6lv4Xm1dQNwRKP8WfoOnltbNwBHNMy31vt5H9q6ATiigb6V3sv70tYNwBEN9bX1Pt6ntm4Ajmiwr6X38L61dQNwRMP9UT2fG9q6ATiiAf+Ins0dbd0AHNGIv0fP5J62bgCOaMzfomdxV1s3AEc06pfoGdzX1g3AEY37a/otO9q6ATiikX9Jv2NLWzcARzT06t+zqa0bgCMavPD5nLZuAI4QPi/R1g3AEcLnJdq6AYAhbd0AwJC2bgBgSFs3ADCkrRsAGNLWDQAMaesGAIa0dQMAQ9q6AYAhbd0AwJC2bgBgSFs3ADCkrRsAGNLWDQAMaesGAIa0dQMAQ9q6AYAhbd0AwJC2bgBgSFs3ADCkrRsAGNLWDQAMaesGAIa0dQMAQ9q6AYAhbd0AwJC2bgBgSFs3ADCkrRsAGNLWDQAMaesGAIa0dQMAQ9q6AYAhbd0AwJC2bgBgSFs3ADCkrRsAGNLWDQAMaev/GwBggwGAYQYAhv07AMCmPwFtns6pep7uQgAAAABJRU5ErkJggg==");
}

namespace Scripts;

public enum ImageType
{
    /// <summary>
    ///   For use by CI builds and tests
    /// </summary>
    CI,

    /// <summary>
    ///   Local build image for making packages not depend on the host system they are compiled on
    /// </summary>
    Builder,
}

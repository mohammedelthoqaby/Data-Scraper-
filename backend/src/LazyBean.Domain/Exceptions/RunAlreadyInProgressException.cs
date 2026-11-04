namespace LazyBean.Domain.Exceptions;

public class RunAlreadyInProgressException()
    : Exception("A collection run is already queued or running. Only one run at a time is allowed.");

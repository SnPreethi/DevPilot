namespace DevPilot.Contracts;

public interface IModelManager
{
    ModelDescriptor Resolve(ExecutionProviderKind provider);
}

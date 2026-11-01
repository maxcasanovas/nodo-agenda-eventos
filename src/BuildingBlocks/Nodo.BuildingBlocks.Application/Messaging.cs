using MediatR;

namespace Nodo.BuildingBlocks.Application;

public interface ICommand : IRequest<Unit>;

public interface ICommand<out TResult> : IRequest<TResult>;

public interface IQuery<out TResult> : IRequest<TResult>;

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand;

public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>;

public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>;

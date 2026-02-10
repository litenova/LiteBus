using LiteBus.Commands.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Samples.Commands;
using LiteBus.Samples.Queries;
using LiteBus.Samples.Requests;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Samples.NetCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    private readonly ICommandMediator _commandMediator;
    private readonly IQueryMediator _queryMediator;

    public OrdersController(ICommandMediator commandMediator, IQueryMediator queryMediator)
    {
        _commandMediator = commandMediator;
        _queryMediator = queryMediator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var command = new PlaceOrderCommand(
            request.CustomerId,
            request.LineItems.Select(x =>
                new PlaceOrderLineItemDto(x.ProductId, x.Quantity, x.UnitPrice)).ToList());

        var orderId = await _commandMediator.SendAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetOrderById), new { id = orderId }, orderId);
    }

    [HttpPost("{orderId:guid}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmOrder(Guid orderId, CancellationToken cancellationToken)
    {
        var command = new ConfirmOrderCommand(orderId);
        await _commandMediator.SendAsync(command, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var query = new GetOrderByIdQuery(id);
        var result = await _queryMediator.QueryAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpGet("customer/{customerId}")]
    [ProducesResponseType(typeof(IEnumerable<OrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrdersByCustomer(string customerId, CancellationToken cancellationToken)
    {
        var query = new GetOrdersByCustomerQuery(customerId);
        var results = await _queryMediator.QueryAsync(query, cancellationToken);
        return Ok(results);
    }
}
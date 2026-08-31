using ErongoIT.Backup.Application.Customers;
using Microsoft.AspNetCore.Authorization;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Customer>>> GetAll(
        CancellationToken cancellationToken)
    {
        var customers = await _customerService.GetAllAsync(
            cancellationToken);

        return Ok(customers);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Customer>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var customer = await _customerService.GetByIdAsync(
            id,
            cancellationToken);

        if (customer is null)
            return NotFound();

        return Ok(customer);
    }

    [HttpPost]
    public async Task<ActionResult<Customer>> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await _customerService.CreateAsync(
            request.Name,
            request.ContactEmail,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = customer.Id },
            customer);
    }

    [HttpPut("{id:guid}/contact-email")]
    public async Task<IActionResult> UpdateContactEmail(
        Guid id,
        [FromBody] UpdateContactEmailRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await _customerService.UpdateContactEmailAsync(
            id,
            request.ContactEmail,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _customerService.ActivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _customerService.DeactivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }
}

public sealed record CreateCustomerRequest(
    string Name,
    string? ContactEmail);

public sealed record UpdateContactEmailRequest(
    string? ContactEmail);

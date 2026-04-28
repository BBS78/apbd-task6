namespace ClinicAppointmentsApi.DTOs;

public class ErrorResponseDto
{
    public string Message { get; set; } = string.Empty;

    public ErrorResponseDto(string message)
    {
        Message = message;
    }
}
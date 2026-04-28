using System.Data;
using ClinicAppointmentsApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicAppointmentsApi.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string DefaultConnection is missing.");
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentListDto>>> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(status) ? DBNull.Value : status;

        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrWhiteSpace(patientLastName) ? DBNull.Value : patientLastName;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<ActionResult<AppointmentDetailsDto>> GetAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,

                p.IdPatient,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,

                d.IdDoctor,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS DoctorSpecialization
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new ErrorResponseDto("Appointment was not found."));
        }

        var appointment = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            DoctorSpecialization = reader.GetString(reader.GetOrdinal("DoctorSpecialization"))
        };

        return Ok(appointment);
    }

    [HttpPost]
    public async Task<ActionResult> CreateAppointment(CreateAppointmentRequestDto request)
    {
        if (request.IdPatient <= 0)
            return BadRequest(new ErrorResponseDto("Patient id must be greater than 0."));

        if (request.IdDoctor <= 0)
            return BadRequest(new ErrorResponseDto("Doctor id must be greater than 0."));

        if (request.AppointmentDate <= DateTime.UtcNow)
            return BadRequest(new ErrorResponseDto("Appointment date cannot be in the past."));

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new ErrorResponseDto("Reason cannot be empty."));

        if (request.Reason.Length > 250)
            return BadRequest(new ErrorResponseDto("Reason cannot be longer than 250 characters."));

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!await ActivePatientExists(connection, request.IdPatient))
            return BadRequest(new ErrorResponseDto("Patient does not exist or is not active."));

        if (!await ActiveDoctorExists(connection, request.IdDoctor))
            return BadRequest(new ErrorResponseDto("Doctor does not exist or is not active."));

        if (await DoctorHasConflict(connection, request.IdDoctor, request.AppointmentDate, null))
            return Conflict(new ErrorResponseDto("Doctor already has a scheduled appointment at this time."));

        await using var command = new SqlCommand("""
            INSERT INTO dbo.Appointments
                (IdPatient, IdDoctor, AppointmentDate, Status, Reason, InternalNotes)
            OUTPUT INSERTED.IdAppointment
            VALUES
                (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason, NULL);
            """, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

        var newIdObj = await command.ExecuteScalarAsync();
        var newId = newIdObj is int value ? value : 0;

        return CreatedAtAction(
            nameof(GetAppointment),
            new { idAppointment = newId },
            new { idAppointment = newId });
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<ActionResult> UpdateAppointment(int idAppointment, UpdateAppointmentRequestDto request)
    {
        if (request.IdPatient <= 0)
            return BadRequest(new ErrorResponseDto("Patient id must be greater than 0."));

        if (request.IdDoctor <= 0)
            return BadRequest(new ErrorResponseDto("Doctor id must be greater than 0."));

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new ErrorResponseDto("Reason cannot be empty."));

        if (request.Reason.Length > 250)
            return BadRequest(new ErrorResponseDto("Reason cannot be longer than 250 characters."));

        if (request.InternalNotes is not null && request.InternalNotes.Length > 500)
            return BadRequest(new ErrorResponseDto("Internal notes cannot be longer than 500 characters."));

        var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };

        if (!allowedStatuses.Contains(request.Status))
            return BadRequest(new ErrorResponseDto("Status must be one of: Scheduled, Completed, Cancelled."));

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var currentAppointment = await GetCurrentAppointment(connection, idAppointment);

        if (currentAppointment is null)
            return NotFound(new ErrorResponseDto("Appointment was not found."));

        if (!await ActivePatientExists(connection, request.IdPatient))
            return BadRequest(new ErrorResponseDto("Patient does not exist or is not active."));

        if (!await ActiveDoctorExists(connection, request.IdDoctor))
            return BadRequest(new ErrorResponseDto("Doctor does not exist or is not active."));

        if (currentAppointment.Value.Status == "Completed"
            && currentAppointment.Value.AppointmentDate != request.AppointmentDate)
        {
            return Conflict(new ErrorResponseDto("Completed appointment date cannot be changed."));
        }

        if (await DoctorHasConflict(connection, request.IdDoctor, request.AppointmentDate, idAppointment))
        {
            return Conflict(new ErrorResponseDto("Doctor already has a scheduled appointment at this time."));
        }

        await using var command = new SqlCommand("""
            UPDATE dbo.Appointments
            SET
                IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = request.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = request.Status;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
        command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value =
            request.InternalNotes is null ? DBNull.Value : request.InternalNotes;

        await command.ExecuteNonQueryAsync();

        return Ok(new { message = "Appointment was updated successfully." });
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<ActionResult> DeleteAppointment(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var currentAppointment = await GetCurrentAppointment(connection, idAppointment);

        if (currentAppointment is null)
            return NotFound(new ErrorResponseDto("Appointment was not found."));

        if (currentAppointment.Value.Status == "Completed")
            return Conflict(new ErrorResponseDto("Completed appointment cannot be deleted."));

        await using var command = new SqlCommand("""
            DELETE FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await command.ExecuteNonQueryAsync();

        return NoContent();
    }

    private static async Task<bool> ActivePatientExists(SqlConnection connection, int idPatient)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Patients
            WHERE IdPatient = @IdPatient
              AND IsActive = 1;
            """, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = idPatient;

        var resultObj = await command.ExecuteScalarAsync();
        var result = resultObj is int value ? value : 0;

        return result > 0;
    }

    private static async Task<bool> ActiveDoctorExists(SqlConnection connection, int idDoctor)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Doctors
            WHERE IdDoctor = @IdDoctor
              AND IsActive = 1;
            """, connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;

        var resultObj = await command.ExecuteScalarAsync();
        var result = resultObj is int value ? value : 0;

        return result > 0;
    }

    private static async Task<bool> DoctorHasConflict(
        SqlConnection connection,
        int idDoctor,
        DateTime appointmentDate,
        int? ignoredAppointmentId)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM dbo.Appointments
            WHERE IdDoctor = @IdDoctor
              AND AppointmentDate = @AppointmentDate
              AND Status = N'Scheduled'
              AND (@IgnoredAppointmentId IS NULL OR IdAppointment <> @IgnoredAppointmentId);
            """, connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointmentDate;
        command.Parameters.Add("@IgnoredAppointmentId", SqlDbType.Int).Value =
            ignoredAppointmentId is null ? DBNull.Value : ignoredAppointmentId;

        var resultObj = await command.ExecuteScalarAsync();
        var result = resultObj is int value ? value : 0;

        return result > 0;
    }

    private static async Task<(string Status, DateTime AppointmentDate)?> GetCurrentAppointment(
        SqlConnection connection,
        int idAppointment)
    {
        await using var command = new SqlCommand("""
            SELECT Status, AppointmentDate
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return (
            reader.GetString(reader.GetOrdinal("Status")),
            reader.GetDateTime(reader.GetOrdinal("AppointmentDate"))
        );
    }
}
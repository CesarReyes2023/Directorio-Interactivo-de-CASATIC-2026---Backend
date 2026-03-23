namespace CasaticDirectorio.Api.DTOs.Socios;

public class SocioUpdateDto
{
    public string? NombreEmpresa { get; set; }
    public string? Slug { get; set; }
    public string? Descripcion { get; set; }
    public List<string>? Especialidades { get; set; }
    public List<string>? Servicios { get; set; }
    public string? RedesSociales { get; set; }
    public string? Telefono { get; set; }
    public string? Direccion { get; set; }
    public string? LogoUrl { get; set; }
    public string? MarcasRepresenta { get; set; }
    public string? EstadoFinanciero { get; set; }
    public bool? Habilitado { get; set; }
    public string? EmailContacto { get; set; }
    public string? MapaUrl { get; set; }
}

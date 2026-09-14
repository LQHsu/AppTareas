import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ProyectoDetalleComponent } from './proyecto-detalle.component';

describe('ProyectoDetalleComponent', () => {
  let component: ProyectoDetalleComponent;
  let fixture: ComponentFixture<ProyectoDetalleComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProyectoDetalleComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ProyectoDetalleComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

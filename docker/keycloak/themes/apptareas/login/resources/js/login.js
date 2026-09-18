/*
 * AppTareas: mejoras al formulario de login que no tienen parametro en
 * las macros base de Keycloak (field.ftl no soporta "placeholder" ni
 * iconos en el label - confirmado leyendo el .ftl real). En vez de
 * sobreescribir login.ftl/field.ftl (compartidos con registro, OTP,
 * reset-password, etc. - tocar el macro global arriesgaria romper esos
 * otros flujos) se hace todo por DOM despues de que Keycloak ya
 * renderizo el formulario. El toggle de mostrar/ocultar NIP YA viene
 * incluido de fabrica en @field.password (passwordVisibility.js del
 * tema base) - nada que hacer ahi.
 */
document.addEventListener('DOMContentLoaded', function () {
  var form = document.getElementById('kc-form-login');
  if (!form) return; // esta pagina no es el login (ej. reset-password)

  var usernameField = document.getElementById('username');
  var passwordField = document.getElementById('password');

  // --- Icono en cada label ---
  function addLabelIcon(fieldId, iconClass) {
    var label = document.querySelector('label[for="' + fieldId + '"] span');
    if (label) {
      label.insertAdjacentHTML(
        'afterbegin',
        '<i class="fas ' + iconClass + '" aria-hidden="true"></i> '
      );
    }
  }
  if (usernameField) addLabelIcon('username', 'fa-id-badge');
  if (passwordField) addLabelIcon('password', 'fa-key');

  // --- Placeholders y atributos del NIP (5 digitos) ---
  if (usernameField) {
    usernameField.placeholder = 'Ej. 12345';
  }
  if (passwordField) {
    passwordField.placeholder = '•••••';
    passwordField.setAttribute('maxlength', '5');
    passwordField.setAttribute('pattern', '\\d{5}');
    passwordField.setAttribute('inputmode', 'numeric');
  }

  // --- Texto de ayuda arriba del formulario ---
  var formWrapper = document.getElementById('kc-form-wrapper');
  if (formWrapper) {
    formWrapper.insertAdjacentHTML(
      'afterbegin',
      '<p class="apptareas-login-hint">Ingresa tu número económico y tu NIP para continuar.</p>'
    );
  }

  // --- Link de recuperacion de NIP (CUS institucional) ---
  // realm.resetPasswordAllowed esta en false a proposito (nuestro SPI
  // no puede actualizar el NIP, lo administra CUSXACDI/CUS) - por eso
  // @field.password nunca renderiza su propio link de "forgot
  // password" aqui. Se agrega el externo real a mano, mismas clases
  // de PatternFly que usa ese bloque nativo para que se vea igual.
  if (passwordField) {
    var passwordGroup = passwordField.closest('.pf-v5-c-form__group');
    if (passwordGroup) {
      passwordGroup.insertAdjacentHTML(
        'afterend',
        '<div class="pf-v5-c-form__helper-text" aria-live="polite">' +
          '<div class="pf-v5-c-helper-text">' +
            '<div class="pf-v5-c-helper-text__item">' +
              '<span class="pf-v5-c-helper-text__item-text">' +
                '<a href="https://cus.xoc.uam.mx/" target="_blank" rel="noopener noreferrer">' +
                  'Recupera tu NIP en CUS' +
                '</a>' +
              '</span>' +
            '</div>' +
          '</div>' +
        '</div>'
      );
    }
  }

  // --- Boton "Acceder" con spinner mientras verifica ---
  // No reemplaza el "login.disabled = true" que ya trae el <form> (ver
  // login.ftl del tema base, atributo onsubmit) - ambos listeners
  // corren, uno deshabilita el boton y este cambia lo que se ve adentro.
  form.addEventListener('submit', function () {
    var btn = document.getElementById('kc-login');
    if (btn) {
      btn.innerHTML =
        '<span class="apptareas-spinner" aria-hidden="true"></span> Verificando…';
    }
  });
});

# frozen_string_literal: true

# Patreon container
class AdminPatreon < HyperComponent
  include Hyperstack::Router::Helpers

  render(DIV) do
    AdminPatreonSettings {}
    HR {}
    Patrons {}
  end
end

# Site admin area with a bunch of fun stuff
class AdminArea < HyperComponent
  include Hyperstack::Router

  render(DIV) do
    unless App.acting_user&.admin?
      H1 { 'You are not an admin' }
      return
    end

    DIV(class: 'container') {
      ReactStrap.Navbar(:dark, color: 'dark', expand: 'md') {
        ReactStrap.Nav(:navbar, className: 'mr-auto') {
          ReactStrap.NavItem {
            NavLink('/admin', exact: true, class: 'nav-link') { 'Status' }
          }

          ReactStrap.NavItem {
            NavLink('/admin/email', class: 'nav-link') { 'Email' }
          }
          ReactStrap.NavItem {
            NavLink('/admin/patreon', class: 'nav-link') { 'Patreon' }
          }
          ReactStrap.NavItem {
            NavLink('/admin/keys', class: 'nav-link') { 'Access Keys' }
          }
        }
      }

      BR {}

      DIV(class: 'container') {
        Switch do
          Route('/admin/status', mounts: SiteStatus)
          Route('/admin', exact: true, mounts: SiteStatus)
          Route('/admin/email', mounts: EmailOptions)
          Route('/admin/patreon', mounts: AdminPatreon)
          Route('/admin/keys', mounts: AccessKeys)
        end
      }
    }
  end
end

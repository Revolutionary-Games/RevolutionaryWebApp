# frozen_string_literal: true

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
        }
      }

      BR {}

      DIV(class: 'container') {
        Switch do
          Route('/admin/status', mounts: SiteStatus)
          Route('/admin', exact: true, mounts: SiteStatus)
          Route('/admin/email', mounts: EmailOptions)
        end
      }
    }
  end
end

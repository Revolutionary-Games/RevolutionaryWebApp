# frozen_string_literal: true

# app/hyperstack/component/app.rb

class CSRF < HyperComponent
  render do
    INPUT('input', type: 'hidden', name: 'authenticity_token',
                   value: Hyperstack::ClientDrivers.opts[:form_authenticity_token])
  end
end

# This is your top level component, the rails router will
# direct all requests to mount this component.  You may
# then use the Route psuedo component to mount specific
# subcomponents depending on the URL.

class App < HyperComponent
  include Hyperstack::Router

  def self.acting_user
    User.find(Hyperstack::Application.acting_user_id) if Hyperstack::Application.acting_user_id
  end

  before_mount do
    @show_navbar = false
  end

  render(DIV, class: 'Container') do
    DIV(class: 'container') {
      ReactStrap.Navbar(:light, color: 'light', expand: 'md') {
        DIV(class: 'navbar navbar-expand-lg navbar-light bg-light') {
          Link('/', class: 'navbar-brand') { 'ThriveDevCenter' }
        }
        ReactStrap.NavbarToggler {}.on(:click) {
          mutate @show_navbar = !@show_navbar
        }
        ReactStrap.Collapse(:navbar, isOpen: @show_navbar) {
          ReactStrap.Nav(:navbar, className: 'mr-auto') {
            ReactStrap.NavItem {
              NavLink('/', exact: true, class: 'nav-link') { 'Home' }
            }

            ReactStrap.NavItem {
              NavLink('/reports', class: 'nav-link') { 'Reports' }
            }

            unless App.acting_user
              ReactStrap.NavItem {
                NavLink('/login', class: 'nav-link') { 'Login' }
              }
            end
            if App.acting_user
              ReactStrap.NavItem {
                NavLink('/symbols', class: 'nav-link') { 'Symbols' }
              }
            end
            ReactStrap.NavItem {
              NavLink('/about', class: 'nav-link') { 'About' }
            }
            ReactStrap.NavItem {
              NavLink('/lfs', class: 'nav-link') { 'Git LFS' }
            }
            if App.acting_user&.admin?
              ReactStrap.NavItem {
                NavLink('/users', class: 'nav-link') { 'Users' }
              }
            end
            if App.acting_user
              ReactStrap.NavItem {
                NavLink('/logout', class: 'nav-link') { 'Logout' }
              }
            end

            # Link('/builds') { 'Releases / Previews' }

            ReactStrap.UncontrolledDropdown(:nav, :inNavBar) {
              ReactStrap.DropdownToggle(:nav, :caret) {
                'Tools'
              }
              ReactStrap.DropdownMenu(:right) {
                ReactStrap.DropdownItem {
                  NavLink('/crashdump-tool') { 'Decode a crashdump' }
                }

                # ReactStrap.DropdownItem(:divider) {}

                # ReactStrap.DropdownItem {
                #   'Something'
                # }
              }
            }
          }
        }
      }
    }

    if App.acting_user
      DIV(class: 'container') do
        SPAN { 'Welcome ' }
        NavLink('/me') { App.acting_user.email }
        SPAN {
          ' You are ' + if App.acting_user.admin?
                          'an admin'
                        elsif App.acting_user.developer?
                          'a developer'
                        else
                          'an user'
                        end
        }
        HR {}
      end
    end

    DIV(class: 'container Content') do
      Switch do
        Route('/symbols', mounts: Symbols)
        Route('/', exact: true, mounts: Home)
        Route('/login', exact: true, mounts: Login)
        Route('/logout', mounts: Logout)
        Route('/about', mounts: About)
        Route('/users', mounts: Users)
        Route('/user/:id', mounts: UserView)
        Route('/me', mounts: CurrentUser)
        Route('/crashdump-tool', mounts: CrashDumpTool)
        Route('/reports', mounts: Reports)
        Route('/report/:id', mounts: ReportView)
        Route('/delete_report/:delete_key', mounts: DeleteReport)
        Route('/lfs', exact: true, mounts: LFSProjects)
        Route('/lfs/:slug', mounts: LfsProjectView)
        Route('/unsubscribe/:type/:key', mounts: Unsubscribe)
      end
    end

    Footer {}
  end
end

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

  render(DIV, class: 'Container') do
    DIV(class: 'Navigation') do
      UL do
        LI { Link('/') { 'Home' } }
        LI { Link('/reports') { 'Reports' } }
        LI { Link('/login') { 'Login' } } unless App.acting_user
        LI { Link('/symbols') { 'Symbols' } } if App.acting_user
        LI { Link('/about') { 'About' } }
        LI { Link('/lfs') { 'Git LFS' } }
        # LI { Link('/builds') { 'Releases / Previews' } }
        LI { Link('/users') { 'Users' } } if App.acting_user&.admin?
        LI { Link('/crashdump-tool') { 'Decode a crashdump' } }
        LI { Link('/logout') { 'Logout' } } if App.acting_user
      end
    end

    if App.acting_user
      DIV do
        SPAN { 'Welcome ' }
        Link('/me') { App.acting_user.email }
      end
    end

    DIV(class: 'Content') do
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
      end
    end

    Footer {}
  end
end

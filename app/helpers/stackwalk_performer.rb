require 'open3'

module StackwalkPerformer

  def self.performStackwalk(file, timeout: 60)
    Open3.popen3("StackWalk/minidump_stackwalk", file, "SymbolData") {
      |stdin, stdout, stderr, wait_thr|

      logger = Rails.logger
      
      logger.debug "Beginning stackwalk on #{file}"
      
      result = ""

      outThread = Thread.new{
        stdout.each {|line|
          result.concat(line)
        }
      }

      errThread = Thread.new{
        stderr.each {|line|
        }
      }

      # Handle timeouts
      if wait_thr.join(timeout) == nil
        logger.error "Stackwalking took more than #{timeout} seconds"
        Process.kill("TERM", wait_thr.pid)
        outThread.kill
        errThread.kill
        return "", true, 0
      end
      
      exit_status = wait_thr.value

      if exit_status != 0
        logger.error "Stackwalking exited with error code"
        return "", false, exit_status
      end

      logger.debug "Stackwalking succeeded"
      outThread.join
      errThread.join
      return result, false, exit_status
    }    
  end

end


